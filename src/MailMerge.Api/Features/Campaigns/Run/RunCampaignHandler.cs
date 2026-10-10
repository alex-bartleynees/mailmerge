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
    public static readonly Error AlreadyRunning = Error.Conflict("Campaign.Running", "This email is already sending");
    public static readonly Error NoAccount = Error.Validation("Campaign.NoAccount", "Choose a sending account first");
    public static readonly Error NoList = Error.Validation("Campaign.NoList", "Choose who to send to first");
    public static readonly Error NoSubject = Error.Validation("Campaign.NoSubject", "Add a subject first");
    public static readonly Error NoBody = Error.Validation("Campaign.NoBody", "Write the message first");

    public static readonly Error NoDrafts = Error.Validation("Campaign.NoDrafts",
        "This account can't save drafts (it has no IMAP settings). Use Send instead.");

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
        var error = campaign switch
        {
            { Status: CampaignStatus.Running } => AlreadyRunning,
            { IsArchived: true } => CampaignErrors.Archived,
            _ when account is null => NoAccount,
            { ListId: null } => NoList,
            _ when string.IsNullOrWhiteSpace(campaign.Subject) => NoSubject,
            _ when string.IsNullOrWhiteSpace(campaign.Body) => NoBody,
            _ when request.Mode == CampaignMode.Draft && !account!.SupportsDrafts => NoDrafts,
            _ => null,
        };
        if (error is not null)
        {
            return error;
        }

        if (await planner.FilterErrorAsync(campaign, ct) is { } filterError)
        {
            return filterError;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await planner.MaterializeAsync(campaign, ct);
        campaign.Status = CampaignStatus.Running;
        campaign.Mode = request.Mode;
        campaign.Note = null;
        campaign.LastActivityAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

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
