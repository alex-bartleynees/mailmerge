using MailMerge.Api.Features.Accounts.Microsoft365;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.MicrosoftSignIn;

public sealed class StartMicrosoftSignInHandler(
    MailMergeDbContext db,
    CurrentUser user,
    MicrosoftSignInFlow flow,
    MicrosoftClientFactory clients,
    TimeProvider clock) : IHandler
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public async Task<Result<StartMicrosoftSignInResponse>> Handle(long accountId, CancellationToken ct)
    {
        var account = await db.SendingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return AccountErrors.NotFound;
        }

        if (account.Kind != AccountKind.Microsoft)
        {
            return AccountErrors.NotMicrosoft;
        }

        if (!clients.Settings.IsConfigured)
        {
            return AccountErrors.MicrosoftNotConfigured;
        }

        var now = clock.GetUtcNow();
        await db.PendingMicrosoftSignIns.Where(s => s.Expires < now).ExecuteDeleteAsync(ct);

        var started = flow.Start(account.FromEmail.Length > 0 ? account.FromEmail : account.MicrosoftAccount);
        db.PendingMicrosoftSignIns.Add(new PendingMicrosoftSignIn
        {
            State = started.State,
            AccountId = account.Id,
            WorkspaceId = user.WorkspaceId,
            UserSubject = user.Subject,
            ProtectedCodeVerifier = started.ProtectedVerifier,
            Expires = now + Lifetime,
        });
        await db.SaveChangesAsync(ct);
        return new StartMicrosoftSignInResponse(started.AuthorizeUrl);
    }
}
