using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.RemoveAttachment;

public sealed class RemoveAttachmentHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long campaignId, long attachmentId, CancellationToken ct)
    {
        var status = await db.Campaigns.Where(c => c.Id == campaignId).Select(c => (CampaignStatus?)c.Status)
            .SingleOrDefaultAsync(ct);
        if (status is null)
        {
            return CampaignErrors.NotFound;
        }

        if (status == CampaignStatus.Running)
        {
            return CampaignErrors.RunningEdit;
        }

        var deleted = await db.Attachments.Where(a => a.Id == attachmentId && a.CampaignId == campaignId)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? Result.Failure(CampaignErrors.AttachmentNotFound) : Result.Success();
    }
}
