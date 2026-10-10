using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.MarkMessage;

public sealed class MarkMessageHandler(MailMergeDbContext db, TimeProvider clock) : IHandler
{
    public async Task<Result> Handle(long campaignId, long messageId, MarkMessageRequest request, CancellationToken ct)
    {
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId && m.CampaignId == campaignId, ct);
        if (message is null)
        {
            return CampaignErrors.MessageNotFound;
        }

        var now = clock.GetUtcNow();
        var marked = message.Mark(request.Status, now);
        if (marked.IsFailure)
        {
            return marked;
        }

        var saved = await db.SaveOrConflictAsync(CampaignErrors.MessageChanged, ct);
        if (saved.IsSuccess)
        {
            // Restart the retention clock with a set-based update, like the worker does, rather than loading (and
            // racing) the campaign row it may be updating.
            await db.Campaigns.Where(c => c.Id == campaignId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, now), ct);
        }

        return saved;
    }
}
