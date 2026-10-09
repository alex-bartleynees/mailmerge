using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.SendTest;

internal static class SendTestEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{campaignId:long}/test", Handle);

    private static async Task<Results<Ok<SendTestResponse>, ProblemHttpResult>> Handle(
        long campaignId, SendTestRequest request, SendTestHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, request, ct)).OkOrProblem();
}
