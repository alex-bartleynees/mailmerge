using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Preview;

internal static class PreviewCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{campaignId:long}/preview", Handle);

    private static async Task<Results<Ok<PreviewCampaignResponse>, ProblemHttpResult>> Handle(
        long campaignId, long? contactId, PreviewCampaignHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, contactId, ct)).OkOrProblem();
}
