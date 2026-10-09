namespace MailMerge.Api.Features.Campaigns.Preview;

/// <summary>
/// The email as one contact would receive it, with paging through the list. <c>UnknownFields</c> are merge
/// fields with no matching column (probably typos); <c>EmptyFields</c> have a column but no value for this contact.
/// </summary>
public sealed record PreviewCampaignResponse(
    string To,
    string Subject,
    string Body,
    bool IsHtml,
    long? ContactId,
    long? PreviousId,
    long? NextId,
    int Total,
    IReadOnlyList<string> UnknownFields,
    IReadOnlyList<string> EmptyFields);
