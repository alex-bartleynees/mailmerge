using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Lists.RemoveColumn;

/// <summary>Removes the column and its value from every contact in the list, in one transaction.</summary>
public sealed class RemoveColumnHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<RemoveColumnResponse>> Handle(long listId, string column, CancellationToken ct)
    {
        if (string.Equals(column, ContactList.EmailColumn, StringComparison.OrdinalIgnoreCase))
        {
            return ListErrors.EmailColumnRequired;
        }

        var list = await db.ContactLists.SingleOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return ListErrors.NotFound;
        }

        list.Columns = list.Columns.Where(c => c != column).ToList();
        var contacts = await db.Contacts.Where(c => c.ListId == listId).ToListAsync(ct);
        foreach (var contact in contacts.Where(c => c.Data.ContainsKey(column)))
        {
            contact.Data = contact.Data.Where(kv => kv.Key != column).ToDictionary();
        }

        await db.SaveChangesAsync(ct);
        return new RemoveColumnResponse(list.Columns);
    }
}
