using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.OptOuts.GetAll;

public sealed class GetOptOutsHandler(MailMergeDbContext db, OptOutRegistry registry, CurrentUser user) : IHandler
{
    /// <summary>Newest first. With <paramref name="email"/>, finds that address even if it was erased (matched by hash).</summary>
    public async Task<IReadOnlyList<OptOutView>> Handle(string? email, CancellationToken ct)
    {
        var query = db.OptOuts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(email))
        {
            var hash = (await registry.HasherAsync(user.WorkspaceId, ct))(email);
            query = query.Where(o => o.EmailHash == hash);
        }

        var rows = await query.OrderByDescending(o => o.Added).ToListAsync(ct);
        return rows.Select(OptOutView.From).ToList();
    }
}
