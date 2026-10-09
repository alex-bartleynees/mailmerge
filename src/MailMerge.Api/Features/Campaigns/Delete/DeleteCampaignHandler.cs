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
        var status = await db.Campaigns.Where(c => c.Id == campaignId).Select(c => (CampaignStatus?)c.Status)
            .SingleOrDefaultAsync(ct);
        if (status is null)
        {
            return CampaignErrors.NotFound;
        }

        if (status == CampaignStatus.Running)
        {
            return CampaignErrors.RunningDelete;
        }

        // Guarded on status so a run started in between isn't deleted mid-send.
        var deleted = await db.Campaigns.Where(c => c.Id == campaignId && c.Status != CampaignStatus.Running)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? Result.Failure(CampaignErrors.RunningDelete) : Result.Success();
    }
}
