using System.Security.Claims;
using MailMerge.Api.Features.Accounts.Microsoft365;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.MicrosoftCallback;

/// <summary>
/// Finishes "Sign in with Microsoft". The sign-in must be unexpired, started by the same staff member, who must still
/// belong to the workspace it was started in. The state row is single-use.
/// </summary>
public sealed class MicrosoftCallbackHandler(
    MailMergeDbContext db,
    MicrosoftSignInFlow flow,
    IOptions<AuthSettings> auth,
    TimeProvider clock,
    ILogger<MicrosoftCallbackHandler> log) : IHandler
{
    public async Task<Result<long>> Handle(MicrosoftCallbackRequest request, ClaimsPrincipal user, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.State))
        {
            return AccountErrors.SignInExpired;
        }

        var signIn = await db.PendingMicrosoftSignIns.SingleOrDefaultAsync(s => s.State == request.State, ct);
        if (signIn is null || signIn.Expires < clock.GetUtcNow())
        {
            return AccountErrors.SignInExpired;
        }

        db.PendingMicrosoftSignIns.Remove(signIn);
        await db.SaveChangesAsync(ct);

        if (signIn.UserSubject != user.Subject())
        {
            return AccountErrors.SignInWrongUser;
        }

        var group = await db.Workspaces.Where(w => w.Id == signIn.WorkspaceId).Select(w => w.KeycloakGroup).SingleAsync(ct);
        if (!user.WorkspaceGroups(auth.Value.WorkspaceGroupPrefix).Contains(group))
        {
            return AccountErrors.SignInWrongUser;
        }

        if (request.Error is not null || string.IsNullOrEmpty(request.Code))
        {
            return AccountErrors.SignInFailed(request.ErrorDescription ?? request.Error ?? "no code returned");
        }

        MicrosoftSignInFlow.Completed completed;
        try
        {
            completed = await flow.CompleteAsync(request.Code, signIn.ProtectedCodeVerifier, ct);
        }
        catch (MsalException e)
        {
            log.LogWarning(e, "Microsoft code redemption failed for account {AccountId}", signIn.AccountId);
            return AccountErrors.SignInFailed(e.Message);
        }

        var account = await db.SendingAccounts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(a => a.Id == signIn.AccountId && a.WorkspaceId == signIn.WorkspaceId, ct);
        if (account is null)
        {
            return AccountErrors.NotFound;
        }

        account.MicrosoftTokenCache = completed.ProtectedCache;
        account.MicrosoftAccount = completed.Mailbox;
        account.MicrosoftAccountId = completed.AccountId;
        account.SignedInBy = user.Email() ?? signIn.UserSubject;
        if (account.FromEmail.Length == 0)
        {
            account.FromEmail = completed.Mailbox;
        }

        await db.SaveChangesAsync(ct);
        return account.Id;
    }
}
