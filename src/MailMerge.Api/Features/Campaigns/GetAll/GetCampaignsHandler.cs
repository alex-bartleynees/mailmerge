using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;

namespace MailMerge.Api.Features.Campaigns.GetAll;

public sealed class GetCampaignsHandler(MailMergeDbContext db) : IHandler
{
    public Task<IReadOnlyList<CampaignDetails>> Handle(CancellationToken ct) =>
        CampaignDetails.LoadAsync(db, db.Campaigns.OrderByDescending(c => c.Id), ct);
}
