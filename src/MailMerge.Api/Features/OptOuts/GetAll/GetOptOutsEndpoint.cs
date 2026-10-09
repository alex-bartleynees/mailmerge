using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.OptOuts.GetAll;

internal static class GetOptOutsEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("", Handle);

    private static async Task<Ok<IReadOnlyList<OptOutView>>> Handle(
        string? email, GetOptOutsHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(email, ct));
}
