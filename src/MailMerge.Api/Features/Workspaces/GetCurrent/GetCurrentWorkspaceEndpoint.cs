using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Workspaces.GetCurrent;

internal static class GetCurrentWorkspaceEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("", Handle);

    private static async Task<Ok<GetCurrentWorkspaceResponse>> Handle(GetCurrentWorkspaceHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(ct));
}
