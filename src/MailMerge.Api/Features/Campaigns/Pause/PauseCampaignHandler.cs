using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Pause;

/// <summary>Stops after the message in flight, if any. Pausing something that isn't running is a no-op.</summary>
public sealed class PauseCampaignHandler(MailMergeDbContext db, TimeProvider clock) : IHandler
{
    public async Task<Result> Handle(long campaignId, CancellationToken ct)
    {
        if (!await db.Campaigns.AnyAsync(c => c.Id == campaignId, ct))
        {
            return CampaignErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        await db.Campaigns.Where(c => c.Id == campaignId && c.Status == CampaignStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CampaignStatus.Paused)
                .SetProperty(c => c.Note, (string?)null)
                .SetProperty(c => c.LastActivityAt, now), ct);
        return Result.Success();
    }
}
