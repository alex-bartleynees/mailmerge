using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Update;

public sealed class UpdateCampaignHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<CampaignDetails>> Handle(long campaignId, UpdateCampaignRequest request, CancellationToken ct)
    {
        var campaign = await db.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        if (campaign.Status == CampaignStatus.Running)
        {
            return CampaignErrors.RunningEdit;
        }

        if (campaign.IsArchived)
        {
            return CampaignErrors.Archived;
        }

        if (request.AccountId is { } accountId && !await db.SendingAccounts.AnyAsync(a => a.Id == accountId, ct))
        {
            return CampaignErrors.AccountNotFound;
        }

        if (request.ListId is { } listId && !await db.ContactLists.AnyAsync(l => l.Id == listId, ct))
        {
            return CampaignErrors.ListNotFound;
        }

        if (request.Name is { } name)
        {
            campaign.Name = string.IsNullOrWhiteSpace(name) ? "New email" : name.Trim();
        }

        campaign.AccountId = request.AccountId ?? campaign.AccountId;
        campaign.ListId = request.ListId ?? campaign.ListId;
        campaign.Subject = request.Subject ?? campaign.Subject;
        campaign.Body = request.Body ?? campaign.Body;
        campaign.IsHtml = request.IsHtml ?? campaign.IsHtml;
        campaign.UnsubscribeFooter = request.UnsubscribeFooter ?? campaign.UnsubscribeFooter;
        await db.SaveChangesAsync(ct);

        return (await CampaignDetails.LoadOneAsync(db, campaignId, ct))!;
    }
}
