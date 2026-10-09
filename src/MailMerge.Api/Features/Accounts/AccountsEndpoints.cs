using MailMerge.Api.Features.Accounts.Check;
using MailMerge.Api.Features.Accounts.Create;
using MailMerge.Api.Features.Accounts.Delete;
using MailMerge.Api.Features.Accounts.GetAll;
using MailMerge.Api.Features.Accounts.MicrosoftCallback;
using MailMerge.Api.Features.Accounts.MicrosoftSignIn;
using MailMerge.Api.Features.Accounts.MicrosoftSignOut;
using MailMerge.Api.Features.Accounts.TestOutbox;
using MailMerge.Api.Features.Accounts.Update;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Accounts;

public sealed class AccountsEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/accounts", "Accounts");
        GetAccountsEndpoint.Map(group);
        CreateAccountEndpoint.Map(group);
        UpdateAccountEndpoint.Map(group);
        DeleteAccountEndpoint.Map(group);
        CheckAccountEndpoint.Map(group);
        StartMicrosoftSignInEndpoint.Map(group);
        SignOutMicrosoftEndpoint.Map(group);
        GetTestOutboxEndpoint.Map(group);

        // The browser returns here from Microsoft. The workspace comes from the stored sign-in, not the request,
        // so this route skips the workspace filter (it still requires the MailMerge role).
        var callback = app.MapGroup("/api/accounts/microsoft")
            .RequireAuthorization(MailMergeAuthorization.Policy)
            .WithTags("Accounts");
        MicrosoftCallbackEndpoint.Map(callback);
    }
}
