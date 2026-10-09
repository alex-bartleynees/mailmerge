using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.Messages;

internal static class GetMessagesEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{campaignId:long}/messages", Handle);

    private static async Task<Results<Ok<IReadOnlyList<MessageView>>, ProblemHttpResult>> Handle(
        long campaignId, MessageStatus? status, int? limit, GetMessagesHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, status, limit, ct)).OkOrProblem();
}
