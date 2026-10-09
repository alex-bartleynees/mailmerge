using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.GetPlan;

internal static class GetPlanEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{campaignId:long}/plan", Handle);

    private static async Task<Results<Ok<GetPlanResponse>, ProblemHttpResult>> Handle(
        long campaignId, GetPlanHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, ct)).OkOrProblem();
}
