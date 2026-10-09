using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Messages;

public sealed class GetMessagesHandler(MailMergeDbContext db) : IHandler
{
    public const int MaxLimit = 5000;

    public async Task<Result<IReadOnlyList<MessageView>>> Handle(
        long campaignId, MessageStatus? status, int? limit, CancellationToken ct)
    {
        if (!await db.Campaigns.AnyAsync(c => c.Id == campaignId, ct))
        {
            return CampaignErrors.NotFound;
        }

        var query = db.Messages.AsNoTracking().Where(m => m.CampaignId == campaignId);
        if (status is { } s)
        {
            query = query.Where(m => m.Status == s);
        }

        return await query.OrderBy(m => m.Id)
            .Take(Math.Clamp(limit ?? 500, 1, MaxLimit))
            .Select(m => new MessageView(m.Id, m.Email, m.Status, m.Error, m.Updated))
            .ToListAsync(ct);
    }
}
