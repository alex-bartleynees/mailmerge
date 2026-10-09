using System.Text.RegularExpressions;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.OptOuts.Add;

public sealed partial class AddOptOutsHandler(
    MailMergeDbContext db,
    OptOutRegistry registry,
    CurrentUser user,
    TimeProvider clock) : IHandler
{
    public async Task<AddOptOutsResponse> Handle(AddOptOutsRequest request, CancellationToken ct)
    {
        var tokens = Separators().Split(request.Emails).Where(t => t.Length > 0).ToList();
        var emails = tokens.Where(EmailAddress.IsValid).Select(EmailAddress.Normalize).Distinct().ToList();
        var ignored = tokens.Where(t => !EmailAddress.IsValid(t)).ToList();

        var hash = await registry.HasherAsync(user.WorkspaceId, ct);
        var byHash = emails.ToDictionary(hash);
        var existing = await db.OptOuts.Where(o => byHash.Keys.Contains(o.EmailHash)).Select(o => o.EmailHash).ToListAsync(ct);

        var now = clock.GetUtcNow();
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        foreach (var (h, email) in byHash.Where(kv => !existing.Contains(kv.Key)))
        {
            db.OptOuts.Add(new OptOut
            {
                WorkspaceId = user.WorkspaceId,
                EmailHash = h,
                Email = email,
                Added = now,
                Reason = reason,
                Source = OptOutSource.Manual,
            });
        }

        await db.SaveChangesAsync(ct);
        return new AddOptOutsResponse(byHash.Count - existing.Count, existing.Count, ignored);
    }

    [GeneratedRegex(@"[\s,;]+")]
    private static partial Regex Separators();
}
