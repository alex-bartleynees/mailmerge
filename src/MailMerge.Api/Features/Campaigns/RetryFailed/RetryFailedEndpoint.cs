using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.RetryFailed;

internal static class RetryFailedEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{campaignId:long}/retry-failed", Handle);

    private static async Task<Results<Ok<RetryFailedResponse>, ProblemHttpResult>> Handle(
        long campaignId, RetryFailedHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, ct)).OkOrProblem();
}
