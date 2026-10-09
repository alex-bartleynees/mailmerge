using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;

namespace MailMerge.Api.Features.Accounts.Create;

public sealed class CreateAccountHandler(
    MailMergeDbContext db,
    CurrentUser user,
    AccountSecretsProtector protector,
    TimeProvider clock) : IHandler
{
    public async Task<AccountView> Handle(CreateAccountRequest request, CancellationToken ct)
    {
        var account = new SendingAccount
        {
            WorkspaceId = user.WorkspaceId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "My account" : request.Name.Trim(),
            Kind = request.Kind,
            FromEmail = (request.FromEmail ?? "").Trim(),
            FromName = (request.FromName ?? "").Trim(),
            PerMinute = request.PerMinute ?? 20,
            Smtp = request.Kind == AccountKind.Smtp ? request.Smtp : null,
            ProtectedSecrets = request.Kind == AccountKind.Smtp
                ? protector.Protect(new AccountSecrets(Blank(request.Password), Blank(request.ImapPassword)))
                : null,
            Created = clock.GetUtcNow(),
        };
        db.SendingAccounts.Add(account);
        await db.SaveChangesAsync(ct);
        return AccountView.From(account, protector);
    }

    private static string? Blank(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
