using System.Collections.Concurrent;
using System.Security.Cryptography;
using MailMerge.Api.Features.Workspaces;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MailMerge.Api.Shared.Auth;

/// <summary>
/// Maps the caller's Keycloak workspace group (e.g. <c>/mailmerge/max-wilson</c>) to a workspace row, creating it
/// on first use, and fills <see cref="CurrentUser"/>. A user in several workspaces picks one with the
/// <c>X-Workspace</c> header (full group path or its last segment); otherwise the first alphabetically is used.
/// </summary>
internal sealed class WorkspaceFilter(
    CurrentUser currentUser,
    MailMergeDbContext db,
    WorkspaceDirectory directory,
    IOptions<AuthSettings> settings) : IEndpointFilter
{
    public const string WorkspaceHeader = "X-Workspace";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var user = http.User;
        var subject = user.Subject();
        if (string.IsNullOrEmpty(subject))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Unauthorized",
                detail: "Your sign-in is missing a user id. Please sign in again.");
        }

        var groups = user.WorkspaceGroups(settings.Value.WorkspaceGroupPrefix);
        var group = Choose(groups, http.Request.Headers[WorkspaceHeader].ToString());
        if (group is null)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Forbidden",
                detail: groups.Count == 0
                    ? "Your account isn't linked to a shop yet. Ask your administrator to add you to one."
                    : "You don't have access to that shop.");
        }

        var workspace = await directory.ResolveAsync(db, group, http.RequestAborted);
        currentUser.Resolve(subject, user.Email(), user.DisplayName(), workspace.Id, workspace.Name, group);
        return await next(context);
    }

    private static string? Choose(IReadOnlyList<string> groups, string requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return groups.FirstOrDefault();
        }

        requested = requested.Trim();
        return groups.FirstOrDefault(g => g == requested || g[(g.LastIndexOf('/') + 1)..] == requested);
    }
}

/// <summary>Process-wide cache of group path → workspace. Workspaces are never deleted, so entries never go stale.</summary>
internal sealed class WorkspaceDirectory(TimeProvider clock)
{
    private readonly ConcurrentDictionary<string, (long Id, string Name)> _cache = new(StringComparer.Ordinal);

    public async Task<(long Id, string Name)> ResolveAsync(MailMergeDbContext db, string group, CancellationToken ct)
    {
        if (_cache.TryGetValue(group, out var hit))
        {
            return hit;
        }

        var name = DefaultName(group);
        // ON CONFLICT makes concurrent first requests from the same shop safe.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO workspaces (keycloak_group, name, retention_days, opt_out_hash_key, created)
             VALUES ({group}, {name}, {Workspace.DefaultRetentionDays}, {RandomNumberGenerator.GetBytes(32)}, {clock.GetUtcNow()})
             ON CONFLICT (keycloak_group) DO NOTHING
             """, ct);

        var row = await db.Workspaces.AsNoTracking()
            .Where(w => w.KeycloakGroup == group)
            .Select(w => new { w.Id, w.Name })
            .SingleAsync(ct);

        var entry = (row.Id, row.Name);
        _cache[group] = entry;
        return entry;
    }

    public void Forget(string group) => _cache.TryRemove(group, out _);

    /// <summary><c>/mailmerge/max-wilson</c> → <c>Max Wilson</c>; staff can rename it later.</summary>
    internal static string DefaultName(string group)
    {
        var slug = group[(group.LastIndexOf('/') + 1)..];
        var words = slug.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        var name = string.Join(' ', words);
        return name.Length == 0 ? slug : name;
    }
}
