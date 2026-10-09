using SharedKernel.Results;

namespace MailMerge.Api.Features.Privacy;

public static class PrivacyErrors
{
    public static readonly Error InvalidEmail = Error.Validation("Privacy.InvalidEmail", "Enter the person's email address");

    public static readonly Error SendingNow = Error.Conflict("Privacy.SendingNow",
        "This person is being emailed right now. Try again in a minute.");
}
