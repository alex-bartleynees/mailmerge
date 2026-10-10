using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Copy;

/// <summary>
/// "Copy as new email": same account, list, subject, body and attachments, with no send history. The way to
/// resend an archived email, or reuse last month's newsletter.
/// </summary>
public sealed class CopyCampaignHandler(MailMergeDbContext db, CurrentUser user, TimeProvider clock) : IHandler
{
    public async Task<Result<CampaignDetails>> Handle(long campaignId, CancellationToken ct)
    {
        var source = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (source is null)
        {
            return CampaignErrors.NotFound;
        }

        var now = clock.GetUtcNow();
        var copy = source.CopyAsNew(now);
        db.Campaigns.Add(copy);
        await db.SaveChangesAsync(ct);

        // Attachments whose content was purged by retention can't be copied.
        var attachments = await db.Attachments.AsNoTracking()
            .Where(a => a.CampaignId == campaignId && a.Content != null).ToListAsync(ct);
        foreach (var a in attachments)
        {
            db.Attachments.Add(new Attachment
            {
                WorkspaceId = user.WorkspaceId,
                CampaignId = copy.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Size,
                Content = a.Content,
            });
        }

        await db.SaveChangesAsync(ct);
        return (await CampaignDetails.LoadOneAsync(db, copy.Id, ct))!;
    }
}
