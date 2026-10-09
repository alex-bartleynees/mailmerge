namespace MailMerge.Api.Features.Privacy.ErasePerson;

/// <summary>
/// <c>DraftsToDeleteInOutlook</c> lists unsent drafts for this person that are still in the shop's mailbox. MailMerge
/// doesn't delete mailbox items; staff remove those in Outlook.
/// </summary>
public sealed record ErasePersonResponse(
    int ContactsDeleted,
    int MessagesDeleted,
    int ImportsDeleted,
    bool KeptOptedOut,
    IReadOnlyList<ErasePersonResponse.Draft> DraftsToDeleteInOutlook)
{
    public sealed record Draft(string CampaignName, string? Account);
}
