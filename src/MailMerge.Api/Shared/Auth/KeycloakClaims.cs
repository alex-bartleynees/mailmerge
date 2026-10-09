using System.Security.Claims;
using System.Text.Json;

namespace MailMerge.Api.Shared.Auth;

/// <summary>
/// Reads Keycloak's token shape. With <c>MapInboundClaims = false</c> the JWT handler keeps claim names
/// as-is: <c>resource_access</c> arrives as one JSON-valued claim and <c>groups</c> as one claim per group.
/// </summary>
public static class KeycloakClaims
{
    public static string? Subject(this ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    public static string? Email(this ClaimsPrincipal user) =>
        user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;

    public static string? DisplayName(this ClaimsPrincipal user) =>
        user.FindFirst("name")?.Value ?? user.FindFirst("preferred_username")?.Value;

    public static bool HasClientRole(this ClaimsPrincipal user, string clientId, string role)
    {
        foreach (var claim in user.FindAll("resource_access"))
        {
            try
            {
                using var doc = JsonDocument.Parse(claim.Value);
                if (doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty(clientId, out var client)
                    && client.TryGetProperty("roles", out var roles)
                    && roles.ValueKind == JsonValueKind.Array
                    && roles.EnumerateArray().Any(r => r.GetString() == role))
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                // A malformed claim grants nothing.
            }
        }

        return false;
    }

    /// <summary>Workspace group paths the user belongs to, sorted so the default choice is stable.</summary>
    public static IReadOnlyList<string> WorkspaceGroups(this ClaimsPrincipal user, string prefix) =>
        user.FindAll("groups")
            .Select(c => c.Value.Trim())
            .Where(g => g.StartsWith(prefix, StringComparison.Ordinal) && g.Length > prefix.Length)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
}
