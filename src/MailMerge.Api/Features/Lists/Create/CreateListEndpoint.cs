using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.Create;

internal static class CreateListEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("", Handle);

    private static async Task<Created<ListSummary>> Handle(CreateListRequest request, CreateListHandler handler, CancellationToken ct)
    {
        var list = await handler.Handle(request, ct);
        return TypedResults.Created($"/api/lists/{list.Id}", list);
    }
}
