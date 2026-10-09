using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.GetAll;

internal static class GetCampaignsEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("", Handle);

    private static async Task<Ok<IReadOnlyList<CampaignDetails>>> Handle(GetCampaignsHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(ct));
}
