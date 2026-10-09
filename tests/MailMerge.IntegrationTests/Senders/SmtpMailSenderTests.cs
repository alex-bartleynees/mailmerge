using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailMerge.IntegrationTests.Senders;

/// <summary>SmtpMailSender against GreenMail. Ports test_smtp_send_with_attachment and adds IMAP drafts.</summary>
public sealed class SmtpMailSenderTests(GreenMailContainer mail) : IClassFixture<GreenMailContainer>
{
    private SmtpMailSender Sender(bool imap = true, string password = GreenMailContainer.Password) => new(
        new SendingAccount
        {
            Id = 1,
            Name = "Shop",
            Kind = AccountKind.Smtp,
            FromEmail = "shop@shop.test",
            FromName = "The Shop",
            Smtp = new SmtpSettings
            {
                Host = mail.Host, Port = mail.SmtpPort, Security = "none", Username = GreenMailContainer.User,
                ImapHost = imap ? mail.Host : "", ImapPort = mail.ImapPort, ImapSecurity = "none",
            },
        },
        new AccountSecrets(password, null),
        TimeProvider.System,
        NullLogger<SmtpMailSender>.Instance);

    private static OutgoingMessage To(string recipient, string subject) =>
        new(recipient, subject, "Hello there", false,
            [new OutgoingAttachment("price list.txt", "text/plain", "hello attachment"u8.ToArray())],
            "https://mailmerge.test/u/abc");

    private async Task<ImapClient> Imap(string user, string password)
    {
        var client = new ImapClient();
        await client.ConnectAsync(mail.Host, mail.ImapPort, SecureSocketOptions.None);
        await client.AuthenticateAsync(user, password);
        return client;
    }

    private async Task<IList<MimeKit.MimeMessage>> Folder(string name)
    {
        using var client = await Imap(GreenMailContainer.User, GreenMailContainer.Password);
        var personal = client.GetFolder(client.PersonalNamespaces[0]);
        var folder = (await personal.GetSubfoldersAsync(false)).FirstOrDefault(f => f.Name == name);
        if (folder is null)
        {
            return [];
        }

        await folder.OpenAsync(FolderAccess.ReadOnly);
        var messages = new List<MimeKit.MimeMessage>();
        for (var i = 0; i < folder.Count; i++)
        {
            messages.Add(await folder.GetMessageAsync(i));
        }

        return messages;
    }

    [Fact]
    public async Task Send_now_delivers_with_attachment_and_unsubscribe_headers_and_keeps_a_sent_copy()
    {
        var recipient = $"ann-{Guid.NewGuid():N}@example.com";

        await Sender().SendNowAsync(To(recipient, "Spring sale"), CancellationToken.None);

        using var inbox = await Imap(recipient, recipient);
        await inbox.Inbox.OpenAsync(FolderAccess.ReadOnly);
        var received = await inbox.Inbox.GetMessageAsync(0);
        received.Subject.Should().Be("Spring sale");
        received.From.ToString().Should().Contain("The Shop").And.Contain("shop@shop.test");
        received.Attachments.Should().ContainSingle().Which.ContentDisposition.FileName.Should().Be("price list.txt");
        received.Headers["List-Unsubscribe"].Should().Be("<https://mailmerge.test/u/abc>");
        received.Headers["List-Unsubscribe-Post"].Should().Be("List-Unsubscribe=One-Click");

        (await Folder("Sent")).Should().Contain(m => m.Subject == "Spring sale");
    }

    [Fact]
    public async Task Draft_lands_in_drafts_and_sending_it_moves_it_to_sent()
    {
        var recipient = $"bob-{Guid.NewGuid():N}@example.com";
        var subject = "Draft " + Guid.NewGuid().ToString("N")[..6];
        var sender = Sender();

        var draftId = await sender.CreateDraftAsync(To(recipient, subject), CancellationToken.None);
        (await Folder("Drafts")).Should().Contain(m => m.Subject == subject);

        await sender.SendDraftAsync(draftId, CancellationToken.None);

        (await Folder("Drafts")).Should().NotContain(m => m.Subject == subject);
        (await Folder("Sent")).Should().Contain(m => m.Subject == subject);
        using var inbox = await Imap(recipient, recipient);
        await inbox.Inbox.OpenAsync(FolderAccess.ReadOnly);
        inbox.Inbox.Count.Should().Be(1);
    }

    [Fact]
    public async Task Sending_a_draft_that_is_gone_is_uncertain()
    {
        var act = () => Sender().SendDraftAsync("<missing@shop.test>", CancellationToken.None);

        await act.Should().ThrowAsync<DeliveryUncertainException>();
    }

    [Fact]
    public async Task Without_imap_drafts_are_unavailable()
    {
        var sender = Sender(imap: false);

        sender.SupportsDrafts.Should().BeFalse();
        await sender.Invoking(s => s.CreateDraftAsync(To("x@example.com", "x"), CancellationToken.None))
            .Should().ThrowAsync<MessageRejectedException>();
        (await sender.CheckAsync(CancellationToken.None)).Should().Contain("No IMAP settings");
    }

    [Fact]
    public async Task Wrong_password_is_an_account_problem()
    {
        var act = () => Sender(password: "wrong").CheckAsync(CancellationToken.None);

        await act.Should().ThrowAsync<AccountProblemException>();
    }

    [Fact]
    public async Task Check_confirms_smtp_and_imap()
    {
        (await Sender().CheckAsync(CancellationToken.None)).Should().Contain("IMAP works");
    }
}
