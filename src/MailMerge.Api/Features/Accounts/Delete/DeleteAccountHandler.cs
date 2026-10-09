using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.Delete;

/// <summary>Removes the account and its saved sign-in. Emails that used it keep their history.</summary>
public sealed class DeleteAccountHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long accountId, CancellationToken ct)
    {
        if (!await db.SendingAccounts.AnyAsync(a => a.Id == accountId, ct))
        {
            return AccountErrors.NotFound;
        }

        if (await db.Campaigns.AnyAsync(c => c.AccountId == accountId && c.Status == CampaignStatus.Running, ct))
        {
            return AccountErrors.InUse;
        }

        await db.SendingAccounts.Where(a => a.Id == accountId).ExecuteDeleteAsync(ct);
        return Result.Success();
    }
}
