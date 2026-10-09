using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Delete;

internal static class DeleteCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapDelete("{campaignId:long}", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long campaignId, DeleteCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, ct)).NoContentOrProblem();
}
