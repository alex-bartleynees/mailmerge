using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace MailMerge.Api.Features.Accounts.Microsoft365;

/// <summary>
/// Builds an MSAL confidential client whose user token cache is one account's encrypted blob. After any MSAL call,
/// <see cref="CacheBinding.ChangedCache"/> holds the re-encrypted blob to persist, if the cache changed.
/// </summary>
public sealed class MicrosoftClientFactory(IOptions<MicrosoftSettings> settings, IDataProtectionProvider protection)
{
    private readonly IDataProtector _protector = protection.CreateProtector("MailMerge.MicrosoftTokenCache.v1");

    public MicrosoftSettings Settings => settings.Value;

    public sealed class CacheBinding
    {
        public byte[]? ChangedCache { get; internal set; }
    }

    public (IConfidentialClientApplication App, CacheBinding Cache) Create(byte[]? protectedCache, string? redirectUri = null)
    {
        var s = settings.Value;
        var builder = ConfidentialClientApplicationBuilder.Create(s.ClientId)
            .WithClientSecret(s.ClientSecret)
            .WithAuthority($"{s.AuthorityHost.TrimEnd('/')}/{s.Tenant}");
        if (redirectUri is not null)
        {
            builder = builder.WithRedirectUri(redirectUri);
        }

        var app = builder.Build();
        var binding = new CacheBinding();
        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (protectedCache is not null)
            {
                args.TokenCache.DeserializeMsalV3(_protector.Unprotect(protectedCache));
            }
        });
        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (args.HasStateChanged)
            {
                protectedCache = _protector.Protect(args.TokenCache.SerializeMsalV3());
                binding.ChangedCache = protectedCache;
            }
        });
        return (app, binding);
    }
}
