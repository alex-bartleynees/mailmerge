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

        if (campaign.IsArchived)
        {
            return CampaignErrors.Archived;
        }

        var now = clock.GetUtcNow();
        var requeued = await db.Messages.Where(m => m.CampaignId == campaignId && m.Status == MessageStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, MessageStatus.Pending)
                .SetProperty(m => m.Error, (string?)null)
                .SetProperty(m => m.Updated, now), ct);
        return new RetryFailedResponse(requeued);
    }
}
