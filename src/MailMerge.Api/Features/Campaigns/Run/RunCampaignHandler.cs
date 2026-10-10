using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Run;

/// <summary>Starts (or resumes) a campaign. Re-running only reaches people who haven't been drafted or sent yet.</summary>
public sealed class RunCampaignHandler(
    MailMergeDbContext db,
    DeliveryPlanner planner,
    DeliverySignal signal,
    TimeProvider clock) : IHandler
{
    public async Task<Result<RunCampaignResponse>> Handle(long campaignId, RunCampaignRequest request, CancellationToken ct)
    {
        var campaign = await db.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var account = campaign.AccountId is { } accountId
            ? await db.SendingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct)
            : null;
        var allowed = campaign.CanStart(request.Mode, account);
        if (allowed.IsFailure)
        {
            return allowed.Error;
        }

        if (await planner.FilterErrorAsync(campaign, ct) is { } filterError)
        {
            return filterError;
        }

        var started = campaign.Start(request.Mode, account, clock.GetUtcNow());
        if (started.IsFailure)
        {
            return started.Error;
        }

        // The campaign's new status and its recipient rows are written in one atomic, conflict-checked save.
        await planner.MaterializeAsync(campaign, ct);
        var saved = await db.SaveOrConflictAsync(CampaignErrors.Changed, ct);
        if (saved.IsFailure)
        {
            return saved.Error;
        }

        signal.Notify();
        return new RunCampaignResponse(await CountsAsync(campaignId, ct));
    }

    private async Task<Dictionary<string, int>> CountsAsync(long campaignId, CancellationToken ct)
    {
        var counts = await db.Messages.Where(m => m.CampaignId == campaignId)
            .GroupBy(m => m.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return CampaignDetails.StatusCounts(counts.Select(c => (c.Key, c.Count)));
    }
}
