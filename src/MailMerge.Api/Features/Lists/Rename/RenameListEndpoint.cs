using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.Rename;

internal static class RenameListEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPut("{listId:long}", Handle);

    private static async Task<Results<Ok<ListSummary>, ProblemHttpResult>> Handle(
        long listId, RenameListRequest request, RenameListHandler handler, CancellationToken ct) =>
        (await handler.Handle(listId, request, ct)).OkOrProblem();
}
