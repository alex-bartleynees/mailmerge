using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace MailMerge.Api.Features.Accounts;

/// <summary>Encrypts SMTP/IMAP passwords at rest with ASP.NET Data Protection (keys live in Postgres).</summary>
public sealed class AccountSecretsProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("MailMerge.AccountSecrets.v1");

    public AccountSecrets Unprotect(SendingAccount account) =>
        account.ProtectedSecrets is { } bytes
            ? JsonSerializer.Deserialize<AccountSecrets>(_protector.Unprotect(bytes))!
            : new AccountSecrets(null, null);

    public byte[]? Protect(AccountSecrets secrets) =>
        secrets.Password is null && secrets.ImapPassword is null
            ? null
            : _protector.Protect(JsonSerializer.SerializeToUtf8Bytes(secrets));
}
