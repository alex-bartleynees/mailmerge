using System.ComponentModel.DataAnnotations;

namespace MailMerge.Api.Shared.Auth;

/// <summary>Keycloak access-token validation and the claims that grant access to MailMerge.</summary>
public sealed class AuthSettings
{
    public const string SectionName = "Auth";

    /// <summary>Realm URL, e.g. <c>https://identity.example.com/realms/mailmerge</c>.</summary>
    [Required]
    public string Authority { get; set; } = "";

    /// <summary>Audience added to access tokens by a Keycloak audience mapper.</summary>
    [Required]
    public string Audience { get; set; } = "mailmerge-api";

    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Keycloak client whose roles are checked (<c>resource_access.{client}.roles</c>).</summary>
    [Required]
    public string RoleClientId { get; set; } = "mailmerge";

    [Required]
    public string RequiredRole { get; set; } = "mailmerge-user";

    /// <summary>Group path prefix that identifies a workspace, e.g. <c>/mailmerge/max-wilson</c>.</summary>
    [Required]
    public string WorkspaceGroupPrefix { get; set; } = "/mailmerge/";
}
