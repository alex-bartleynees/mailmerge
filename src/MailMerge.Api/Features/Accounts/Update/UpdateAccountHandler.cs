using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.Update;

public sealed class UpdateAccountHandler(MailMergeDbContext db, AccountSecretsProtector protector) : IHandler
{
    public async Task<Result<AccountView>> Handle(long accountId, UpdateAccountRequest request, CancellationToken ct)
    {
        var account = await db.SendingAccounts.SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return AccountErrors.NotFound;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            account.Name = request.Name.Trim();
        }

        account.FromEmail = request.FromEmail?.Trim() ?? account.FromEmail;
        account.FromName = request.FromName?.Trim() ?? account.FromName;
        account.PerMinute = request.PerMinute ?? account.PerMinute;

        if (account.Kind == AccountKind.Smtp)
        {
            account.Smtp = request.Smtp ?? account.Smtp;
            var saved = protector.Unprotect(account);
            account.ProtectedSecrets = protector.Protect(new AccountSecrets(
                string.IsNullOrEmpty(request.Password) ? saved.Password : request.Password,
                string.IsNullOrEmpty(request.ImapPassword) ? saved.ImapPassword : request.ImapPassword));
        }

        await db.SaveChangesAsync(ct);
        return AccountView.From(account, protector);
    }
}
