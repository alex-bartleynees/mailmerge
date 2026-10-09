using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MailMerge.IntegrationTests.Infrastructure;

/// <summary>
/// Stands in for Keycloak JWT validation. Builds the same claim shape a real access token produces with
/// <c>MapInboundClaims = false</c>: <c>sub</c>, <c>email</c>, a JSON <c>resource_access</c> claim and one
/// <c>groups</c> claim per group. Request headers choose the identity.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string NoAuthHeader = "X-Test-NoAuth";
    public const string SubjectHeader = "X-Test-Sub";
    public const string EmailHeader = "X-Test-Email";

    /// <summary>Comma-separated group paths, or <see cref="None"/>.</summary>
    public const string GroupsHeader = "X-Test-Groups";

    /// <summary>Comma-separated client roles on the "mailmerge" client, or <see cref="None"/>.</summary>
    public const string RolesHeader = "X-Test-Roles";

    /// <summary>HttpClient drops empty header values, so "no roles/groups" is spelled out.</summary>
    public const string None = "none";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(NoAuthHeader))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var subject = Header(SubjectHeader) ?? "test-user";
        var email = Header(EmailHeader) ?? $"{subject}@example.test";
        var roles = List(RolesHeader, "mailmerge-user");
        var groups = List(GroupsHeader, "/mailmerge/test-shop");

        var claims = new List<Claim>
        {
            new("sub", subject),
            new("email", email),
            new("name", subject),
            new("resource_access", JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["mailmerge"] = new { roles },
                ["account"] = new { roles = new[] { "view-profile" } },
            }), "JSON"),
        };
        claims.AddRange(groups.Select(g => new Claim("groups", g)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName)));
    }

    private string? Header(string name) =>
        Request.Headers.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : null;

    private string[] List(string name, string fallback)
    {
        if (!Request.Headers.TryGetValue(name, out var v))
        {
            return [fallback];
        }

        return v.ToString() == None
            ? []
            : v.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
