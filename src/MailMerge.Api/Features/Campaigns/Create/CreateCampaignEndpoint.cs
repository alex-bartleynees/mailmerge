using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Create;

internal static class CreateCampaignEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("", Handle);

    private static async Task<Created<CampaignDetails>> Handle(
        CreateCampaignRequest request, CreateCampaignHandler handler, CancellationToken ct)
    {
        var campaign = await handler.Handle(request, ct);
        return TypedResults.Created($"/api/campaigns/{campaign.Id}", campaign);
    }
}
