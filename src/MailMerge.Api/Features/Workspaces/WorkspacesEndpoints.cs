using MailMerge.Api.Features.Workspaces.GetCurrent;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Workspaces;

public sealed class WorkspacesEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/workspace", "Workspace");
        GetCurrentWorkspaceEndpoint.Map(group);
    }
}
