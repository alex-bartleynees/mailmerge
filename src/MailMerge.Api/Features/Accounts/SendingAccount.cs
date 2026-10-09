using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Accounts;

public enum AccountKind
{
    /// <summary>Microsoft 365 / Outlook via Microsoft Graph (drafts land in Outlook's Drafts folder).</summary>
    Microsoft,

    /// <summary>Any other mail server: SMTP to send, optional IMAP for Drafts and Sent copies.</summary>
    Smtp,

    /// <summary>Test mode: writes nothing anywhere real.</summary>
    Test,
}

/// <summary>A mailbox the shop sends from. Shared by everyone in the workspace.</summary>
public sealed class SendingAccount : IWorkspaceOwned
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public required string Name { get; set; }
    public AccountKind Kind { get; set; }
    public string FromEmail { get; set; } = "";
    public string FromName { get; set; } = "";
    public int PerMinute { get; set; } = 20;

    /// <summary>Non-secret SMTP/IMAP settings. Null for Microsoft and Test accounts.</summary>
    public SmtpSettings? Smtp { get; set; }

    /// <summary>SMTP/IMAP passwords, encrypted with Data Protection. Never returned by the API.</summary>
    public byte[]? ProtectedSecrets { get; set; }

    /// <summary>MSAL token cache, encrypted with Data Protection.</summary>
    public byte[]? MicrosoftTokenCache { get; set; }

    /// <summary>The Microsoft mailbox signed in, e.g. sales@shop.co.nz.</summary>
    public string? MicrosoftAccount { get; set; }

    /// <summary>Staff member who connected the Microsoft mailbox.</summary>
    public string? SignedInBy { get; set; }

    /// <summary>Earliest time the worker may send the next message from this account (pacing).</summary>
    public DateTimeOffset? NextSendAt { get; set; }

    public DateTimeOffset Created { get; set; }
}

public sealed record SmtpSettings
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 587;

    /// <summary><c>starttls</c>, <c>ssl</c> or <c>none</c>.</summary>
    public string Security { get; init; } = "starttls";

    public string Username { get; init; } = "";
    public string ImapHost { get; init; } = "";
    public int ImapPort { get; init; } = 993;
    public string ImapSecurity { get; init; } = "ssl";

    /// <summary>Blank means "same as the SMTP username".</summary>
    public string ImapUsername { get; init; } = "";

    /// <summary>Optional folder overrides; blank means auto-detect.</summary>
    public string DraftsFolder { get; init; } = "";

    public string SentFolder { get; init; } = "";

    public bool HasImap => !string.IsNullOrWhiteSpace(ImapHost);
}

public sealed record AccountSecrets(string? Password, string? ImapPassword);

internal sealed class SendingAccountConfiguration : IEntityTypeConfiguration<SendingAccount>
{
    public void Configure(EntityTypeBuilder<SendingAccount> b)
    {
        b.HasIndex(a => a.WorkspaceId);
        b.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Name).HasMaxLength(200);
        b.Property(a => a.FromEmail).HasMaxLength(254);
        b.Property(a => a.FromName).HasMaxLength(200);
        b.Property(a => a.Smtp).HasJsonConversion();
    }
}
