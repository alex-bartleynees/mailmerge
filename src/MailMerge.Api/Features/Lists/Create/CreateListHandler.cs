using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;

namespace MailMerge.Api.Features.Lists.Create;

public sealed class CreateListHandler(MailMergeDbContext db, CurrentUser user, TimeProvider clock) : IHandler
{
    public async Task<ListSummary> Handle(CreateListRequest request, CancellationToken ct)
    {
        var list = new ContactList
        {
            WorkspaceId = user.WorkspaceId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "New list" : request.Name.Trim(),
            Columns = [ContactList.EmailColumn, "FirstName", "LastName"],
            Created = clock.GetUtcNow(),
        };
        db.ContactLists.Add(list);
        await db.SaveChangesAsync(ct);
        return new ListSummary(list.Id, list.Name, list.Columns, 0, list.Created);
    }
}
