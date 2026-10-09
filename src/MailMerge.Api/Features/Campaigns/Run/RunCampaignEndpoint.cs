using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Run;

internal static class RunCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{campaignId:long}/run", Handle);

    private static async Task<Results<Ok<RunCampaignResponse>, ProblemHttpResult>> Handle(
        long campaignId, RunCampaignRequest request, RunCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, request, ct)).OkOrProblem();
}
