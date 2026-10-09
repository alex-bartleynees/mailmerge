using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.OptOuts.Add;

internal static class AddOptOutsEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("", Handle);

    private static async Task<Ok<AddOptOutsResponse>> Handle(
        AddOptOutsRequest request, AddOptOutsHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(request, ct));
}
