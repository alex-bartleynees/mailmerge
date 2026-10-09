using MailMerge.Api.Shared.Auth;

namespace MailMerge.Api.Shared.Http;

public static class ApiGroups
{
    /// <summary>
    /// A staff-facing route group: requires the MailMerge role, resolves the workspace, validates requests.
    /// Every feature router starts here.
    /// </summary>
    public static RouteGroupBuilder MapStaffGroup(this IEndpointRouteBuilder app, string prefix, string tag) =>
        app.MapGroup(prefix)
            .RequireAuthorization(MailMergeAuthorization.Policy)
            .AddEndpointFilter<WorkspaceFilter>()
            .WithRequestValidation()
            .WithTags(tag);
}
