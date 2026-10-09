using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Lists.GetAll;

public sealed class GetListsHandler(MailMergeDbContext db) : IHandler
{
    public async Task<IReadOnlyList<ListSummary>> Handle(CancellationToken ct) =>
        await db.ContactLists.AsNoTracking()
            .OrderBy(l => l.Id)
            .Select(l => new ListSummary(l.Id, l.Name, l.Columns, db.Contacts.Count(c => c.ListId == l.Id), l.Created))
            .ToListAsync(ct);
}
