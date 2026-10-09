using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.MarkMessage;

public sealed class MarkMessageHandler(MailMergeDbContext db, TimeProvider clock) : IHandler
{
    public static readonly Error NotFound = Error.NotFound("Message.NotFound", "Message not found");

    public static readonly Error NotUnresolved =
        Error.Conflict("Message.NotUnresolved", "Only unresolved messages can be changed");

    private static readonly MessageStatus[] Unresolved =
        [MessageStatus.Uncertain, MessageStatus.Failed, MessageStatus.Skipped, MessageStatus.Pending];

    public async Task<Result> Handle(long campaignId, long messageId, MarkMessageRequest request, CancellationToken ct)
    {
        var message = await db.Messages.SingleOrDefaultAsync(m => m.Id == messageId && m.CampaignId == campaignId, ct);
        if (message is null)
        {
            return NotFound;
        }

        if (!Unresolved.Contains(message.Status))
        {
            return NotUnresolved;
        }

        var now = clock.GetUtcNow();
        message.Status = request.Status;
        message.Error = request.Status == MessageStatus.Pending ? null : "Marked by you";
        message.Updated = now;
        await db.Campaigns.Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, now), ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
