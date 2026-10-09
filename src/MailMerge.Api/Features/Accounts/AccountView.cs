namespace MailMerge.Api.Features.Accounts;

/// <summary>A sending account as staff see it. Passwords are never returned, only whether one is saved.</summary>
public sealed record AccountView(
    long Id,
    string Name,
    AccountKind Kind,
    string FromEmail,
    string FromName,
    int PerMinute,
    SmtpSettings? Smtp,
    bool HasPassword,
    bool HasImapPassword,
    string? MicrosoftAccount,
    string? SignedInBy,
    bool Connected,
    bool SupportsDrafts)
{
    public static AccountView From(SendingAccount a, AccountSecretsProtector protector)
    {
        var secrets = protector.Unprotect(a);
        var connected = a.Kind switch
        {
            AccountKind.Microsoft => a.MicrosoftTokenCache is not null,
            AccountKind.Smtp => !string.IsNullOrWhiteSpace(a.Smtp?.Host),
            _ => true,
        };
        return new AccountView(a.Id, a.Name, a.Kind, a.FromEmail, a.FromName, a.PerMinute, a.Smtp,
            !string.IsNullOrEmpty(secrets.Password), !string.IsNullOrEmpty(secrets.ImapPassword),
            a.MicrosoftAccount, a.SignedInBy, connected, a.SupportsDrafts);
    }
}
