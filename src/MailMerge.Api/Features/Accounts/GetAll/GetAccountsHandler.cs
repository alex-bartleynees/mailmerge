using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Accounts.GetAll;

public sealed class GetAccountsHandler(MailMergeDbContext db, AccountSecretsProtector protector) : IHandler
{
    public async Task<IReadOnlyList<AccountView>> Handle(CancellationToken ct) =>
        (await db.SendingAccounts.AsNoTracking().OrderBy(a => a.Id).ToListAsync(ct))
        .Select(a => AccountView.From(a, protector))
        .ToList();
}
