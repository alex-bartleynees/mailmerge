using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace MailMerge.Api.Features.Accounts.Sending;

/// <summary>
/// Any mail server: SMTP to send, plus optional IMAP so drafts land in the Drafts folder and sent mail gets a copy
/// in Sent (SMTP alone doesn't do that). Without IMAP, only "send now" is available.
/// </summary>
public sealed class SmtpMailSender(
    SendingAccount account,
    AccountSecrets secrets,
    TimeProvider clock,
    ILogger<SmtpMailSender> log) : IMailSender
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);
    private static readonly string[] DraftsFallbacks = ["Drafts", "INBOX.Drafts", "Draft"];
    private static readonly string[] SentFallbacks = ["Sent", "INBOX.Sent", "Sent Items", "Sent Messages"];

    private SmtpSettings Settings => account.Smtp ?? new SmtpSettings();

    public bool SupportsDrafts => Settings.HasImap;

    private string FromEmail => account.FromEmail.Length > 0 ? account.FromEmail : Settings.Username;

    public async Task<string> CreateDraftAsync(OutgoingMessage message, CancellationToken ct)
    {
        if (!SupportsDrafts)
        {
            throw new MessageRejectedException(
                "This account has no IMAP settings, so drafts can't be saved to the mailbox.");
        }

        var mime = Build(message);
        using var imap = await ConnectImapAsync(ct);
        var drafts = await FolderAsync(imap, SpecialFolder.Drafts, Settings.DraftsFolder, DraftsFallbacks, ct);
        await drafts.OpenAsync(FolderAccess.ReadWrite, ct);
        await drafts.AppendAsync(mime, MessageFlags.Draft | MessageFlags.Seen, ct);
        await imap.DisconnectAsync(true, ct);
        return mime.MessageId ?? throw new InvalidOperationException("Draft has no Message-ID");
    }

    public async Task SendDraftAsync(string draftId, CancellationToken ct)
    {
        using var imap = await ConnectImapAsync(ct);
        var drafts = await FolderAsync(imap, SpecialFolder.Drafts, Settings.DraftsFolder, DraftsFallbacks, ct);
        await drafts.OpenAsync(FolderAccess.ReadWrite, ct);
        var ids = await drafts.SearchAsync(SearchQuery.HeaderContains("Message-ID", draftId), ct);
        if (ids.Count == 0)
        {
            throw new DeliveryUncertainException(
                "The draft isn't in the Drafts folder any more (already sent or deleted?). Check Sent Items.");
        }

        var mime = await drafts.GetMessageAsync(ids[0], ct);
        await SendAsync(mime, ct);

        // Sent. Everything below is housekeeping and must never fail the message.
        try
        {
            var sent = await FolderAsync(imap, SpecialFolder.Sent, Settings.SentFolder, SentFallbacks, ct);
            await sent.AppendAsync(mime, MessageFlags.Seen, ct);
            await drafts.OpenAsync(FolderAccess.ReadWrite, ct);
            await drafts.AddFlagsAsync(ids[0], MessageFlags.Deleted, true, ct);
            await drafts.ExpungeAsync(ct);
            await imap.DisconnectAsync(true, ct);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Sent OK but couldn't tidy IMAP folders for account {AccountId}", account.Id);
        }
    }

    public async Task SendNowAsync(OutgoingMessage message, CancellationToken ct)
    {
        var mime = Build(message);
        await SendAsync(mime, ct);
        if (!SupportsDrafts)
        {
            return;
        }

        try
        {
            using var imap = await ConnectImapAsync(ct);
            var sent = await FolderAsync(imap, SpecialFolder.Sent, Settings.SentFolder, SentFallbacks, ct);
            await sent.AppendAsync(mime, MessageFlags.Seen, ct);
            await imap.DisconnectAsync(true, ct);
        }
        catch (Exception e)
        {
            log.LogWarning(e, "Sent OK but couldn't copy to the Sent folder for account {AccountId}", account.Id);
        }
    }

    public async Task<string> CheckAsync(CancellationToken ct)
    {
        using (var smtp = await ConnectSmtpAsync(ct))
        {
            await smtp.DisconnectAsync(true, ct);
        }

        if (!SupportsDrafts)
        {
            return "Mail server login works. No IMAP settings, so sent mail won't appear in the Sent folder " +
                   "and drafts are unavailable.";
        }

        using var imap = await ConnectImapAsync(ct);
        await imap.DisconnectAsync(true, ct);
        return "Mail server login works, and IMAP works (drafts and Sent copies will be saved).";
    }

    private MimeMessage Build(OutgoingMessage message) =>
        MimeBuilder.Build(message, FromEmail, account.FromName, clock.GetUtcNow());

    private async Task SendAsync(MimeMessage mime, CancellationToken ct)
    {
        using var smtp = await ConnectSmtpAsync(ct);
        try
        {
            await smtp.SendAsync(mime, ct);
        }
        catch (SmtpCommandException e) when (e.ErrorCode is SmtpErrorCode.RecipientNotAccepted
                                                 or SmtpErrorCode.SenderNotAccepted
                                                 or SmtpErrorCode.MessageNotAccepted)
        {
            throw new MessageRejectedException($"The mail server rejected this message: {e.Message}", e);
        }

        // Anything else (dropped connection, timeout) propagates: the worker marks the message uncertain rather
        // than risk sending it twice.
        try
        {
            await smtp.DisconnectAsync(true, ct);
        }
        catch (Exception e)
        {
            log.LogDebug(e, "SMTP disconnect failed after a successful send");
        }
    }

    private async Task<SmtpClient> ConnectSmtpAsync(CancellationToken ct)
    {
        var s = Settings;
        if (string.IsNullOrWhiteSpace(s.Host))
        {
            throw new AccountProblemException("The SMTP server isn't set up for this account.");
        }

        var client = new SmtpClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(s.Host, s.Port, Security(s.Security), ct);
            if (!string.IsNullOrEmpty(s.Username))
            {
                await client.AuthenticateAsync(s.Username, secrets.Password ?? "", ct);
            }

            return client;
        }
        catch (AuthenticationException e)
        {
            client.Dispose();
            throw new AccountProblemException(
                "The mail server rejected the username/password. Many providers need an app password.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            client.Dispose();
            throw new AccountProblemException($"Couldn't connect to the mail server: {e.Message}", e);
        }
    }

    private async Task<ImapClient> ConnectImapAsync(CancellationToken ct)
    {
        var s = Settings;
        var client = new ImapClient { Timeout = (int)Timeout.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(s.ImapHost, s.ImapPort, Security(s.ImapSecurity), ct);
            await client.AuthenticateAsync(
                s.ImapUsername.Length > 0 ? s.ImapUsername : s.Username,
                secrets.ImapPassword ?? secrets.Password ?? "", ct);
            return client;
        }
        catch (AuthenticationException e)
        {
            client.Dispose();
            throw new AccountProblemException("IMAP login failed: check the IMAP username and password.", e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            client.Dispose();
            throw new AccountProblemException($"Couldn't connect to the IMAP server: {e.Message}", e);
        }
    }

    /// <summary>The configured folder, else the server's special-use folder, else a conventional name.</summary>
    private static async Task<IMailFolder> FolderAsync(
        ImapClient imap, SpecialFolder special, string configured, string[] fallbacks, CancellationToken ct)
    {
        if (configured.Length > 0)
        {
            return await imap.GetFolderAsync(configured, ct);
        }

        if ((imap.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0
            && imap.GetFolder(special) is { } folder)
        {
            return folder;
        }

        // Top level plus INBOX children, where servers like Courier/Dovecot (INBOX.Drafts) keep them.
        var personal = imap.GetFolder(imap.PersonalNamespaces[0]);
        var all = (await personal.GetSubfoldersAsync(false, ct))
            .Concat(await imap.Inbox.GetSubfoldersAsync(false, ct))
            .ToList();
        foreach (var name in fallbacks)
        {
            if (all.FirstOrDefault(f => string.Equals(f.FullName, name, StringComparison.OrdinalIgnoreCase)) is { } match)
            {
                return match;
            }
        }

        // Create the conventional folder rather than failing the send.
        return await personal.CreateAsync(fallbacks[0], true, ct)
               ?? throw new AccountProblemException($"Couldn't find or create the {fallbacks[0]} folder.");
    }

    private static SecureSocketOptions Security(string value) => value.ToLowerInvariant() switch
    {
        "ssl" => SecureSocketOptions.SslOnConnect,
        "none" => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls,
    };
}
