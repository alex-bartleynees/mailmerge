using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Copy;

internal static class CopyCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{campaignId:long}/copy", Handle);

    private static async Task<Results<Created<CampaignDetails>, ProblemHttpResult>> Handle(
        long campaignId, CopyCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, ct)).CreatedOrProblem(c => $"/api/campaigns/{c.Id}");
}
