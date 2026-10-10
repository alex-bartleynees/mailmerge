using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Delete;

/// <summary>Deletes the email, its attachments and its per-recipient history. Drafts already in Outlook stay there.</summary>
public sealed class DeleteCampaignHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long campaignId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var allowed = campaign.EnsureDeletable();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        // The xmin check means a run started in between makes this fail instead of deleting mid-send. Messages and
        // attachments go with it (ON DELETE CASCADE).
        db.Campaigns.Remove(campaign);
        return await db.SaveOrConflictAsync(CampaignErrors.Changed, ct);
    }
}
