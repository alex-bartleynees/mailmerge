using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.AddColumn;

internal static class AddColumnEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{listId:long}/columns", Handle);

    private static async Task<Results<Ok<AddColumnResponse>, ProblemHttpResult>> Handle(
        long listId, AddColumnRequest request, AddColumnHandler handler, CancellationToken ct) =>
        (await handler.Handle(listId, request, ct)).OkOrProblem();
}
