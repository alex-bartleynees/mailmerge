using System.Text;
using MailMerge.Api.Host;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MailMerge.Api.Features.OptOuts.Unsubscribe;

/// <summary>
/// Per-recipient unsubscribe links. The token is the (workspace, campaign, address) triple encrypted with Data
/// Protection, so it can't be forged or used to learn anything, and needs no database row until it's used.
/// </summary>
public sealed class UnsubscribeLinks(IDataProtectionProvider protection, IOptions<AppSettings> app)
{
    /// <summary>
    /// Outside <c>/api</c> on purpose: the BFF demands a CSRF header on signed-in <c>/api</c> POSTs, which would break the
    /// confirm button for a staff member clicking the link in their own test email.
    /// </summary>
    public const string PublicPath = "/u";

    private readonly IDataProtector _protector = protection.CreateProtector("MailMerge.Unsubscribe.v1");

    public sealed record Target(long WorkspaceId, long CampaignId, string Email);

    public string Create(long workspaceId, long campaignId, string email)
    {
        var payload = Encoding.UTF8.GetBytes($"{workspaceId}|{campaignId}|{email}");
        return $"{app.Value.PublicBaseUrl.TrimEnd('/')}{PublicPath}/{WebEncoders.Base64UrlEncode(_protector.Protect(payload))}";
    }

    public Target? Read(string token)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(_protector.Unprotect(WebEncoders.Base64UrlDecode(token))).Split('|', 3);
            return new Target(long.Parse(parts[0]), long.Parse(parts[1]), parts[2]);
        }
        catch (Exception e) when (e is FormatException or System.Security.Cryptography.CryptographicException or IndexOutOfRangeException)
        {
            return null;
        }
    }
}
