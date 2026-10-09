using MailMerge.Api.Features.Workspaces.GetCurrent;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Workspaces.Update;

internal static class UpdateWorkspaceEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPut("", Handle);

    private static async Task<Ok<GetCurrentWorkspaceResponse>> Handle(
        UpdateWorkspaceRequest request, UpdateWorkspaceHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(request, ct));
}
