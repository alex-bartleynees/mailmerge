using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.RemoveColumn;

internal static class RemoveColumnEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapDelete("{listId:long}/columns/{column}", Handle);

    private static async Task<Results<Ok<RemoveColumnResponse>, ProblemHttpResult>> Handle(
        long listId, string column, RemoveColumnHandler handler, CancellationToken ct) =>
        (await handler.Handle(listId, Uri.UnescapeDataString(column), ct)).OkOrProblem();
}
