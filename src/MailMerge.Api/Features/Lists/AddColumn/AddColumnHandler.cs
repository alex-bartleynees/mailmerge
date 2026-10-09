using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Lists.AddColumn;

public sealed class AddColumnHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<AddColumnResponse>> Handle(long listId, AddColumnRequest request, CancellationToken ct)
    {
        var list = await db.ContactLists.SingleOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return ListErrors.NotFound;
        }

        var name = request.Name.Trim();
        if (list.Columns.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return ListErrors.ColumnExists;
        }

        list.Columns = [.. list.Columns, name];
        await db.SaveChangesAsync(ct);
        return new AddColumnResponse(list.Columns);
    }
}
