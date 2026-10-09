using MailMerge.Api.Shared.Merging;
using MimeKit.Utils;
using MimeKit;

namespace MailMerge.Api.Features.Accounts.Sending;

/// <summary>Builds the RFC 5322 message used by SMTP, IMAP drafts and test mode.</summary>
public static class MimeBuilder
{
    public static MimeMessage Build(OutgoingMessage message, string fromEmail, string fromName, DateTimeOffset date)
    {
        var mime = new MimeMessage
        {
            Subject = message.Subject,
            Date = date,
            MessageId = MimeUtils.GenerateMessageId(fromEmail.Contains('@') ? fromEmail[(fromEmail.IndexOf('@') + 1)..] : "localhost"),
        };
        mime.From.Add(new MailboxAddress(fromName, fromEmail));
        mime.To.Add(MailboxAddress.Parse(message.To));

        if (message.UnsubscribeUrl is { } url)
        {
            mime.Headers.Add("List-Unsubscribe", $"<{url}>");
            mime.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        var body = new BodyBuilder();
        if (message.IsHtml)
        {
            body.HtmlBody = message.Body;
            body.TextBody = MergeTemplate.HtmlToText(message.Body) is { Length: > 0 } text ? text : " ";
        }
        else
        {
            body.TextBody = message.Body.Length > 0 ? message.Body : " ";
        }

        foreach (var a in message.Attachments)
        {
            body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        }

        mime.Body = body.ToMessageBody();
        return mime;
    }

    public static byte[] ToBytes(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(stream);
        return stream.ToArray();
    }
}
