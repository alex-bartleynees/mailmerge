using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Pause;

/// <summary>Stops after the message in flight, if any. Pausing something that isn't running is a no-op.</summary>
public sealed class PauseCampaignHandler(MailMergeDbContext db, TimeProvider clock) : IHandler
{
    private const int Attempts = 3;

    public async Task<Result> Handle(long campaignId, CancellationToken ct)
    {
        // A running campaign is updated by the worker after every message, so a save can lose the race; reload and
        // re-apply a few times rather than make staff press Pause again.
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            db.ChangeTracker.Clear();
            var campaign = await db.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, ct);
            if (campaign is null)
            {
                return CampaignErrors.NotFound;
            }

            if (!campaign.Pause(clock.GetUtcNow()))
            {
                return Result.Success();
            }

            if ((await db.SaveOrConflictAsync(CampaignErrors.Changed, ct)).IsSuccess)
            {
                return Result.Success();
            }
        }

        return CampaignErrors.Changed;
    }
}
