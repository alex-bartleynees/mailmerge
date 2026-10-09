namespace MailMerge.Api.Features.Campaigns.Delivery;

/// <summary>What pressing "Create drafts" or "Send" would do right now. Nothing is changed by computing it.</summary>
public sealed record DeliveryPlan(
    int Contacts,
    int Skipped,
    int ToDraft,
    int ToSend,
    int AlreadySent,
    int Failed,
    int Uncertain);
