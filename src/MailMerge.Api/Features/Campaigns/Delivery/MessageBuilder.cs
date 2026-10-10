using System.Net;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Campaigns.Delivery;

public sealed class EmptySubjectException() : Exception("The subject is empty.");

/// <summary>Merges a campaign's subject and body for one recipient and adds the optional unsubscribe footer.</summary>
public static class MessageBuilder
{
    public const string TestSubjectPrefix = "[TEST] ";
    public const string DefaultUnsubscribeMessage = "Don't want these emails?";

    public static OutgoingMessage Build(
        Campaign campaign,
        IReadOnlyDictionary<string, string> data,
        string to,
        IReadOnlyList<OutgoingAttachment> attachments,
        string? unsubscribeUrl,
        string subjectPrefix = "",
        BusinessFooter? workspaceFooter = null)
    {
        var subject = (subjectPrefix + MergeTemplate.Render(campaign.Subject, data, asHtml: false)).Trim();
        // Line breaks in a subject would corrupt the message headers.
        subject = subject.ReplaceLineEndings(" ");
        if (subject.Length == 0 || subject == subjectPrefix.Trim())
        {
            throw new EmptySubjectException();
        }

        var body = Body(campaign, data, workspaceFooter, unsubscribeUrl);
        return new OutgoingMessage(to, subject, body, campaign.IsHtml, attachments,
            campaign.UnsubscribeFooter ? unsubscribeUrl : null);
    }

    public static string Body(Campaign campaign, IReadOnlyDictionary<string, string> data,
        BusinessFooter? workspaceFooter, string? unsubscribeUrl)
    {
        var body = MergeTemplate.Render(campaign.Body, data, campaign.IsHtml);
        if (campaign.IncludeBusinessFooter)
        {
            body += (campaign.UseWorkspaceBusinessFooter ? workspaceFooter : campaign.BusinessFooter)?.Render(campaign.IsHtml) ?? "";
        }
        if (campaign.UnsubscribeFooter && unsubscribeUrl is not null)
        {
            body += Footer(unsubscribeUrl, campaign.IsHtml, campaign.UnsubscribeMessage);
        }

        return body;
    }

    public static string Footer(string unsubscribeUrl, bool isHtml, string? message = null)
    {
        var text = string.IsNullOrWhiteSpace(message) ? DefaultUnsubscribeMessage : message.Trim();
        return isHtml
            ? $"""<p style="margin-top:24px;font-size:12px;color:#777">{WebUtility.HtmlEncode(text).ReplaceLineEndings("<br>")} <a href="{WebUtility.HtmlEncode(unsubscribeUrl)}">Unsubscribe</a></p>"""
            : $"\n\n--\n{text} Unsubscribe here: {unsubscribeUrl}\n";
    }
}
