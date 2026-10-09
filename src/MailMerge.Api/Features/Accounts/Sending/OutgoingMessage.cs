namespace MailMerge.Api.Features.Accounts.Sending;

public sealed record OutgoingAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>One fully merged email, ready for a sender.</summary>
public sealed record OutgoingMessage(
    string To,
    string Subject,
    string Body,
    bool IsHtml,
    IReadOnlyList<OutgoingAttachment> Attachments,
    string? UnsubscribeUrl = null);
