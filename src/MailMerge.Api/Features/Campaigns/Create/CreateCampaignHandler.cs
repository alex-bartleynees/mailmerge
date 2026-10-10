using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Campaigns.Create;

/// <summary>New emails start with the first sending account and the newest list, the usual choice in a small shop.</summary>
public sealed class CreateCampaignHandler(MailMergeDbContext db, CurrentUser user, TimeProvider clock) : IHandler
{
    public async Task<CampaignDetails> Handle(CreateCampaignRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var campaign = new Campaign
        {
            WorkspaceId = user.WorkspaceId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "New email" : request.Name.Trim(),
            IsHtml = true,
            AccountId = await db.SendingAccounts.OrderBy(a => a.Id).Select(a => (long?)a.Id).FirstOrDefaultAsync(ct),
            ListId = await db.ContactLists.OrderByDescending(l => l.Id).Select(l => (long?)l.Id).FirstOrDefaultAsync(ct),
            Created = now,
            LastActivityAt = now,
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return (await CampaignDetails.LoadOneAsync(db, campaign.Id, ct))!;
    }
}
