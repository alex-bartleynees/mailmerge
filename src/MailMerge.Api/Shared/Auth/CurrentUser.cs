namespace MailMerge.Api.Shared.Auth;

/// <summary>
/// The signed-in staff member and the workspace (shop) the request acts on. Populated once per request by
/// <see cref="WorkspaceFilter"/>; the DbContext query filters read <see cref="WorkspaceId"/>, so a request that
/// never passed the filter (or background work) sees no workspace data unless it opts out explicitly.
/// </summary>
public sealed class CurrentUser
{
    public string Subject { get; private set; } = "";
    public string? Email { get; private set; }
    public string? Name { get; private set; }
    public long WorkspaceId { get; private set; }
    public string WorkspaceName { get; private set; } = "";

    public bool IsResolved => WorkspaceId != 0;

    /// <summary>Who to record in audit fields: the email when known, else the Keycloak subject.</summary>
    public string DisplayIdentity => Email ?? Subject;

    internal void Resolve(string subject, string? email, string? name, long workspaceId, string workspaceName)
    {
        Subject = subject;
        Email = email;
        Name = name;
        WorkspaceId = workspaceId;
        WorkspaceName = workspaceName;
    }
}
