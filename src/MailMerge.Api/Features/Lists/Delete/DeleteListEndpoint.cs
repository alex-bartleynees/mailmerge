using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.Delete;

internal static class DeleteListEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapDelete("{listId:long}", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long listId, DeleteListHandler handler, CancellationToken ct) =>
        (await handler.Handle(listId, ct)).NoContentOrProblem();
}
