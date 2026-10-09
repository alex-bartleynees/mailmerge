using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MailMerge.Api.Shared.Auth;

/// <summary>
/// Local development without Keycloak or the BFF: every request is a fixed staff member of one workspace, with the
/// same claim shape a Keycloak token produces. Only registered when the environment is Development <em>and</em>
/// <c>Auth:Development:Enabled</c> is true.
/// </summary>
internal static class DevelopmentAuthentication
{
    public const string Scheme = "Development";

    public static bool IsEnabled(IHostEnvironment environment, IConfiguration configuration) =>
        environment.IsDevelopment() && configuration.GetValue<bool>("Auth:Development:Enabled");

    public static void Add(WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(Scheme)
            .AddScheme<AuthenticationSchemeOptions, Handler>(Scheme, _ => { });
    }

    private sealed class Handler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptions<AuthSettings> settings,
        IConfiguration configuration) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var s = settings.Value;
            var workspace = configuration["Auth:Development:Workspace"] ?? "dev-shop";
            var email = configuration["Auth:Development:Email"] ?? "staff@dev.test";
            var claims = new[]
            {
                new Claim("sub", "development-user"),
                new Claim("email", email),
                new Claim("name", "Dev Staff"),
                new Claim("resource_access", JsonSerializer.Serialize(
                    new Dictionary<string, object> { [s.RoleClientId] = new { roles = new[] { s.RequiredRole } } }), "JSON"),
                new Claim("groups", s.WorkspaceGroupPrefix + workspace),
            };
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, DevelopmentAuthentication.Scheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, DevelopmentAuthentication.Scheme)));
        }
    }
}
