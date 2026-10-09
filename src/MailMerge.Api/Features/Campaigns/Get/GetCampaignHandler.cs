using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Get;

public sealed class GetCampaignHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<CampaignDetails>> Handle(long campaignId, CancellationToken ct) =>
        await CampaignDetails.LoadOneAsync(db, campaignId, ct) is { } details ? details : CampaignErrors.NotFound;
}
