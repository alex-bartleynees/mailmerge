using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.MicrosoftSignOut;

/// <summary>Forgets the saved Microsoft sign-in. Running emails from this account will pause until someone signs in.</summary>
public sealed class SignOutMicrosoftHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long accountId, CancellationToken ct)
    {
        var updated = await db.SendingAccounts.Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.MicrosoftTokenCache, (byte[]?)null)
                .SetProperty(a => a.MicrosoftAccount, (string?)null)
                .SetProperty(a => a.MicrosoftAccountId, (string?)null)
                .SetProperty(a => a.SignedInBy, (string?)null), ct);
        return updated == 0 ? Result.Failure(AccountErrors.NotFound) : Result.Success();
    }
}
