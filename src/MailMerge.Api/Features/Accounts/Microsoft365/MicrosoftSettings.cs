namespace MailMerge.Api.Features.Accounts.Microsoft365;

/// <summary>The Entra (Azure AD) app registration MailMerge uses to reach shop mailboxes. See SETUP.md.</summary>
public sealed class MicrosoftSettings
{
    public const string SectionName = "Microsoft";

    /// <summary>Application (client) ID. Blank disables Microsoft accounts.</summary>
    public string ClientId { get; set; } = "";

    public string ClientSecret { get; set; } = "";

    /// <summary><c>organizations</c> for a multitenant app; a tenant id when registered inside the shop's tenant.</summary>
    public string Tenant { get; set; } = "organizations";

    public string AuthorityHost { get; set; } = "https://login.microsoftonline.com";

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com/v1.0";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public static readonly string[] Scopes =
    [
        "https://graph.microsoft.com/Mail.ReadWrite",
        "https://graph.microsoft.com/Mail.Send",
        "https://graph.microsoft.com/User.Read",
    ];
}
