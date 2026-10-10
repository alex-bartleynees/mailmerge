using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Workspaces.GetCurrent;

public sealed class GetCurrentWorkspaceHandler(MailMergeDbContext db, CurrentUser user) : IHandler
{
    public async Task<GetCurrentWorkspaceResponse> Handle(CancellationToken ct)
    {
        var workspace = await db.Workspaces.AsNoTracking()
            .Where(w => w.Id == user.WorkspaceId)
            .Select(w => new { w.RetentionDays, w.BusinessFooter })
            .SingleAsync(ct);

        var hasAccount = await db.SendingAccounts.AnyAsync(ct);
        var accountConnected = await db.SendingAccounts.AnyAsync(a =>
            a.Kind == AccountKind.Test
            || (a.Kind == AccountKind.Microsoft && a.MicrosoftTokenCache != null)
            || (a.Kind == AccountKind.Smtp && a.ProtectedSecrets != null), ct);

        return new GetCurrentWorkspaceResponse(
            new(user.Subject, user.Email, user.Name),
            new(user.WorkspaceId, user.WorkspaceName, workspace.RetentionDays, workspace.BusinessFooter),
            new(hasAccount, accountConnected, await db.Contacts.AnyAsync(ct), await db.Campaigns.AnyAsync(ct)));
    }
}
