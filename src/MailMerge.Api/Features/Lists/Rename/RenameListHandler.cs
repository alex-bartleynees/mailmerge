using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Lists.Rename;

public sealed class RenameListHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<ListSummary>> Handle(long listId, RenameListRequest request, CancellationToken ct)
    {
        var list = await db.ContactLists.SingleOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return ListErrors.NotFound;
        }

        list.Name = request.Name.Trim();
        await db.SaveChangesAsync(ct);
        var count = await db.Contacts.CountAsync(c => c.ListId == listId, ct);
        return new ListSummary(list.Id, list.Name, list.Columns, count, list.Created);
    }
}
