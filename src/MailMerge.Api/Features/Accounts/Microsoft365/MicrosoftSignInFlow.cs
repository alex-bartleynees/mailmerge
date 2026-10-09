using System.Security.Cryptography;
using System.Text;
using MailMerge.Api.Host;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace MailMerge.Api.Features.Accounts.Microsoft365;

/// <summary>
/// "Sign in with Microsoft" using the authorization-code flow with PKCE. The browser goes to Microsoft and comes
/// back to <see cref="CallbackPath"/> (through the BFF, so the callback knows which staff member it is).
/// </summary>
public sealed class MicrosoftSignInFlow(
    MicrosoftClientFactory clients,
    IOptions<AppSettings> app,
    IDataProtectionProvider protection)
{
    public const string CallbackPath = "/api/accounts/microsoft/callback";

    private readonly IDataProtector _verifiers = protection.CreateProtector("MailMerge.MicrosoftSignIn.v1");

    public string RedirectUri => app.Value.PublicBaseUrl.TrimEnd('/') + CallbackPath;

    public sealed record Started(string State, string ProtectedVerifier, string AuthorizeUrl);

    public Started Start(string? loginHint)
    {
        var s = clients.Settings;
        var state = Base64Url(RandomNumberGenerator.GetBytes(32));
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = s.ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = RedirectUri,
            ["response_mode"] = "query",
            ["scope"] = "openid profile offline_access " + string.Join(' ', MicrosoftSettings.Scopes),
            ["state"] = state,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["prompt"] = "select_account",
        };
        if (!string.IsNullOrWhiteSpace(loginHint))
        {
            query["login_hint"] = loginHint;
        }

        var url = QueryHelpers.AddQueryString($"{s.AuthorityHost.TrimEnd('/')}/{s.Tenant}/oauth2/v2.0/authorize", query);
        return new Started(state, _verifiers.Protect(verifier), url);
    }

    public sealed record Completed(string Mailbox, string AccountId, byte[] ProtectedCache);

    /// <summary>Redeems the code. The resulting refresh token lives in the returned (encrypted) MSAL cache.</summary>
    public async Task<Completed> CompleteAsync(string code, string protectedVerifier, CancellationToken ct)
    {
        var (msal, cache) = clients.Create(protectedCache: null, RedirectUri);
        var result = await msal.AcquireTokenByAuthorizationCode(MicrosoftSettings.Scopes, code)
            .WithPkceCodeVerifier(_verifiers.Unprotect(protectedVerifier))
            .ExecuteAsync(ct);
        return new Completed(result.Account.Username, result.Account.HomeAccountId.Identifier, cache.ChangedCache
            ?? throw new InvalidOperationException("Microsoft sign-in returned no tokens to keep."));
    }

    private static string Base64Url(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);
}
