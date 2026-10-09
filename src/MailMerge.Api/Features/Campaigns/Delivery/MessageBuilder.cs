using System.Net;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Campaigns.Delivery;

public sealed class EmptySubjectException() : Exception("The subject is empty.");

/// <summary>Merges a campaign's subject and body for one recipient and adds the optional unsubscribe footer.</summary>
public static class MessageBuilder
{
    public const string TestSubjectPrefix = "[TEST] ";

    public static OutgoingMessage Build(
        Campaign campaign,
        IReadOnlyDictionary<string, string> data,
        string to,
        IReadOnlyList<OutgoingAttachment> attachments,
        string? unsubscribeUrl,
        string subjectPrefix = "")
    {
        var subject = (subjectPrefix + MergeTemplate.Render(campaign.Subject, data, asHtml: false)).Trim();
        // Line breaks in a subject would corrupt the message headers.
        subject = subject.ReplaceLineEndings(" ");
        if (subject.Length == 0 || subject == subjectPrefix.Trim())
        {
            throw new EmptySubjectException();
        }

        var body = MergeTemplate.Render(campaign.Body, data, campaign.IsHtml);
        if (campaign.UnsubscribeFooter && unsubscribeUrl is not null)
        {
            body += Footer(unsubscribeUrl, campaign.IsHtml);
        }

        return new OutgoingMessage(to, subject, body, campaign.IsHtml, attachments,
            campaign.UnsubscribeFooter ? unsubscribeUrl : null);
    }

    public static string Footer(string unsubscribeUrl, bool isHtml) =>
        isHtml
            ? $"""<p style="margin-top:24px;font-size:12px;color:#777">Don't want these emails? <a href="{WebUtility.HtmlEncode(unsubscribeUrl)}">Unsubscribe</a></p>"""
            : $"\n\n--\nDon't want these emails? Unsubscribe here: {unsubscribeUrl}\n";
}
