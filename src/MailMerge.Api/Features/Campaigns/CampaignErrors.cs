using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns;

public static class CampaignErrors
{
    public static readonly Error NotFound = Error.NotFound("Campaign.NotFound", "Email not found");

    public static readonly Error RunningEdit =
        Error.Conflict("Campaign.Running", "Pause the email before editing it");

    public static readonly Error RunningDelete = Error.Conflict("Campaign.Running", "Pause the email first");

    public static readonly Error Archived = Error.Conflict("Campaign.Archived",
        "This email has been archived, so it can't be changed or sent again. Use \"Copy as new email\" instead.");

    public static readonly Error AlreadyRunning = Error.Conflict("Campaign.Running", "This email is already sending");

    public static readonly Error NoAccount = Error.Validation("Campaign.NoAccount", "Choose a sending account first");

    public static readonly Error NoList = Error.Validation("Campaign.NoList", "Choose who to send to first");

    public static readonly Error NoSubject = Error.Validation("Campaign.NoSubject", "Add a subject first");

    public static readonly Error NoBody = Error.Validation("Campaign.NoBody", "Write the message first");

    public static readonly Error NoDrafts = Error.Validation("Campaign.NoDrafts",
        "This account can't save drafts (it has no IMAP settings). Use Send instead.");

    public static readonly Error Changed = Error.Conflict("Campaign.Changed",
        "This email changed while you were working on it (it may have just finished sending). Refresh and try again.");

    public static readonly Error MessageNotFound = Error.NotFound("Message.NotFound", "Message not found");

    public static readonly Error MessageNotUnresolved =
        Error.Conflict("Message.NotUnresolved", "Only unresolved messages can be changed");

    public static readonly Error MessageOutcome =
        Error.Validation("Message.Outcome", "A message can only be marked as sent, retry, or skip");

    public static readonly Error MessageChanged = Error.Conflict("Message.Changed",
        "This message is being processed right now. Refresh and try again.");

    public static readonly Error AccountNotFound =
        Error.Validation("Campaign.AccountNotFound", "That sending account no longer exists");

    public static readonly Error ListNotFound =
        Error.Validation("Campaign.ListNotFound", "That contact list no longer exists");

    public static readonly Error AttachmentNotFound = Error.NotFound("Campaign.AttachmentNotFound", "Attachment not found");

    public static readonly Error NoFile = Error.Validation("Campaign.NoFile", "No file received");

    public static Error AttachmentTooLarge(int megabytes) =>
        Error.Validation("Campaign.AttachmentTooLarge", $"That file is too large (the limit is {megabytes} MB).");
}
