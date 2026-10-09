using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.MarkMessage;

internal static class MarkMessageEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("{campaignId:long}/messages/{messageId:long}/mark", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long campaignId, long messageId, MarkMessageRequest request, MarkMessageHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, messageId, request, ct)).NoContentOrProblem();
}
