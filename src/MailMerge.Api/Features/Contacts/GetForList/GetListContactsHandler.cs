using MailMerge.Api.Features.Lists;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Contacts.GetForList;

public sealed class GetListContactsHandler(MailMergeDbContext db, OptOutRegistry optOuts, CurrentUser user) : IHandler
{
    public async Task<Result<GetListContactsResponse>> Handle(long listId, CancellationToken ct)
    {
        var list = await db.ContactLists.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return ListErrors.NotFound;
        }

        var matcher = await optOuts.LoadAsync(user.WorkspaceId, ct);
        var contacts = await db.Contacts.AsNoTracking().Where(c => c.ListId == listId).OrderBy(c => c.Id).ToListAsync(ct);
        return new GetListContactsResponse(list.Id, list.Name, list.Columns,
            contacts.Select(c => ContactView.From(c, matcher)).ToList());
    }
}
