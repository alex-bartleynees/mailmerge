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

    public static readonly Error AccountNotFound =
        Error.Validation("Campaign.AccountNotFound", "That sending account no longer exists");

    public static readonly Error ListNotFound =
        Error.Validation("Campaign.ListNotFound", "That contact list no longer exists");

    public static readonly Error AttachmentNotFound = Error.NotFound("Campaign.AttachmentNotFound", "Attachment not found");

    public static readonly Error NoFile = Error.Validation("Campaign.NoFile", "No file received");

    public static Error AttachmentTooLarge(int megabytes) =>
        Error.Validation("Campaign.AttachmentTooLarge", $"That file is too large (the limit is {megabytes} MB).");
}
