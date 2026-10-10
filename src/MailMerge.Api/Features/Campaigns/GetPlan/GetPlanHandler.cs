using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.GetPlan;

public sealed class GetPlanHandler(MailMergeDbContext db, DeliveryPlanner planner) : IHandler
{
    public async Task<Result<GetPlanResponse>> Handle(long campaignId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var account = campaign.AccountId is { } id
            ? await db.SendingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct)
            : null;
        return new GetPlanResponse(await planner.PlanAsync(campaign, ct), account?.SupportsDrafts ?? false,
            account?.Name, account?.FromEmail, (await planner.FilterErrorAsync(campaign, ct))?.Detail);
    }
}
