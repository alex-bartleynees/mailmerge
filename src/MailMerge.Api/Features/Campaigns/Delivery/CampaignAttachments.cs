using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Campaigns.Delivery;

internal static class CampaignAttachments
{
    /// <summary>Attachment bytes for sending. Filters by workspace explicitly so the worker can use it too.</summary>
    public static async Task<IReadOnlyList<OutgoingAttachment>> LoadAsync(
        MailMergeDbContext db, Campaign campaign, CancellationToken ct) =>
        await db.Attachments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.WorkspaceId == campaign.WorkspaceId && a.CampaignId == campaign.Id && a.Content != null)
            .OrderBy(a => a.Id)
            .Select(a => new OutgoingAttachment(a.FileName, a.ContentType, a.Content!))
            .ToListAsync(ct);
}
