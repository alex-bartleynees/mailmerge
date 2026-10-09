using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.GetAll;

internal static class GetListsEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("", Handle);

    private static async Task<Ok<IReadOnlyList<ListSummary>>> Handle(GetListsHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(ct));
}
