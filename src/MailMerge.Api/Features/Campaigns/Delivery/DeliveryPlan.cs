namespace MailMerge.Api.Features.Campaigns.Delivery;

/// <summary>
/// What pressing "Create drafts" or "Send" would do right now. Nothing is changed by computing it.
/// <c>Skipped</c> includes <c>FilteredOut</c> (contacts the email's filter excludes).
/// </summary>
public sealed record DeliveryPlan(
    int Contacts,
    int Skipped,
    int FilteredOut,
    int ToDraft,
    int ToSend,
    int AlreadySent,
    int Failed,
    int Uncertain);
