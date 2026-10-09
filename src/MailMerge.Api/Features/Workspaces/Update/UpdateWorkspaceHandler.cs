using MailMerge.Api.Features.Workspaces.GetCurrent;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Workspaces.Update;

internal sealed class UpdateWorkspaceHandler(
    MailMergeDbContext db,
    CurrentUser user,
    WorkspaceDirectory directory,
    GetCurrentWorkspaceHandler current) : IHandler
{
    public async Task<GetCurrentWorkspaceResponse> Handle(UpdateWorkspaceRequest request, CancellationToken ct)
    {
        var workspace = await db.Workspaces.SingleAsync(w => w.Id == user.WorkspaceId, ct);
        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            workspace.Name = request.Name.Trim();
        }

        workspace.RetentionDays = request.RetentionDays ?? workspace.RetentionDays;
        await db.SaveChangesAsync(ct);

        directory.Forget(user.WorkspaceGroup);
        user.Resolve(user.Subject, user.Email, user.Name, workspace.Id, workspace.Name, user.WorkspaceGroup);
        return await current.Handle(ct);
    }
}
