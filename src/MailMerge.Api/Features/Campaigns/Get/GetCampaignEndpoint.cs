using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Get;

internal static class GetCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{campaignId:long}", Handle);

    private static async Task<Results<Ok<CampaignDetails>, ProblemHttpResult>> Handle(
        long campaignId, GetCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, ct)).OkOrProblem();
}
