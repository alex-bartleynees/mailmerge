using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.RetryFailed;

/// <summary>
/// Puts failed rows back to Pending. Uncertain rows are deliberately left alone: they might have been sent, so each
/// one needs a person to check Sent Items and mark it.
/// </summary>
public sealed class RetryFailedHandler(MailMergeDbContext db, TimeProvider clock) : IHandler
{
    public async Task<Result<RetryFailedResponse>> Handle(long campaignId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var allowed = campaign.EnsureCanRequeue();
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        // The worker never touches failed rows, so this can't race it; Requeue holds the "failed only" rule.
        var now = clock.GetUtcNow();
        var failed = await db.Messages.Where(m => m.CampaignId == campaignId && m.Status == MessageStatus.Failed)
            .ToListAsync(ct);
        var requeued = failed.Count(m => m.Requeue(now));
        var saved = await db.SaveOrConflictAsync(CampaignErrors.MessageChanged, ct);
        return saved.IsSuccess ? new RetryFailedResponse(requeued) : saved.Error;
    }
}
