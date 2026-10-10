using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.RemoveAttachment;

public sealed class RemoveAttachmentHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long campaignId, long attachmentId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var editable = campaign.EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        var deleted = await db.Attachments.Where(a => a.Id == attachmentId && a.CampaignId == campaignId)
            .ExecuteDeleteAsync(ct);
        return deleted == 0 ? Result.Failure(CampaignErrors.AttachmentNotFound) : Result.Success();
    }
}
