using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Pause;

internal static class PauseCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{campaignId:long}/pause", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long campaignId, PauseCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, ct)).NoContentOrProblem();
}
