using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Privacy.GetLog;

public sealed class GetPrivacyLogHandler(MailMergeDbContext db) : IHandler
{
    public async Task<IReadOnlyList<PrivacyLogView>> Handle(CancellationToken ct) =>
        await db.PrivacyLog.AsNoTracking().OrderByDescending(e => e.PerformedAt).Take(200)
            .Select(e => new PrivacyLogView(e.Action, e.PerformedBy, e.PerformedAt, e.Counts))
            .ToListAsync(ct);
}
