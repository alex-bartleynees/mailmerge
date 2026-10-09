using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Update;

internal static class UpdateCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPut("{campaignId:long}", Handle);

    private static async Task<Results<Ok<CampaignDetails>, ProblemHttpResult>> Handle(
        long campaignId, UpdateCampaignRequest request, UpdateCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, request, ct)).OkOrProblem();
}
