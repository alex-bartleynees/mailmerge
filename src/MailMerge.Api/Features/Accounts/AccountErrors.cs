using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts;

public static class AccountErrors
{
    public static readonly Error NotFound = Error.NotFound("Account.NotFound", "Account not found");

    public static readonly Error InUse =
        Error.Conflict("Account.InUse", "Pause the emails that are sending from this account first");

    public static readonly Error NotMicrosoft =
        Error.Validation("Account.NotMicrosoft", "Only Microsoft 365 accounts use Microsoft sign-in");

    public static readonly Error NotTest = Error.Validation("Account.NotTest", "Only test accounts have a test outbox");

    public static readonly Error MicrosoftNotConfigured = Error.Validation("Account.MicrosoftNotConfigured",
        "Microsoft sign-in isn't set up on this server yet. Ask whoever installed MailMerge (see SETUP.md).");

    public static readonly Error SignInExpired = Error.Validation("Account.SignInExpired",
        "That Microsoft sign-in took too long or was already used. Please click Sign in again.");

    public static readonly Error SignInWrongUser = Error.Validation("Account.SignInWrongUser",
        "That Microsoft sign-in was started by someone else. Please click Sign in again.");

    public static Error SignInFailed(string reason) =>
        Error.Validation("Account.SignInFailed", $"Microsoft sign-in didn't complete: {reason}");
}
