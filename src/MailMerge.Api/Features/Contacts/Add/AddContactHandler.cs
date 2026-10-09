using MailMerge.Api.Features.Lists;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Contacts.Add;

public sealed class AddContactHandler(MailMergeDbContext db, OptOutRegistry optOuts, CurrentUser user) : IHandler
{
    public async Task<Result<ContactView>> Handle(long listId, AddContactRequest request, CancellationToken ct)
    {
        var (email, data) = ContactData.Clean(request.Data ?? []);
        if (email.Length == 0)
        {
            return ContactErrors.EmailRequired;
        }

        var list = await db.ContactLists.SingleOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return ListErrors.NotFound;
        }

        if (await db.Contacts.AnyAsync(c => c.ListId == listId && c.Email == email, ct))
        {
            return ContactErrors.Duplicate;
        }

        var contact = new Contact { WorkspaceId = user.WorkspaceId, ListId = listId, Email = email, Data = data };
        db.Contacts.Add(contact);
        list.Columns = ContactData.MergeColumns(list.Columns, data.Keys);
        await db.SaveChangesAsync(ct);

        return ContactView.From(contact, await optOuts.LoadAsync(user.WorkspaceId, ct));
    }
}
