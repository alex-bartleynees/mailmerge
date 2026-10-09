using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Contacts.Update;

public sealed class UpdateContactHandler(MailMergeDbContext db, OptOutRegistry optOuts, CurrentUser user) : IHandler
{
    public async Task<Result<ContactView>> Handle(long contactId, UpdateContactRequest request, CancellationToken ct)
    {
        var contact = await db.Contacts.SingleOrDefaultAsync(c => c.Id == contactId, ct);
        if (contact is null)
        {
            return ContactErrors.NotFound;
        }

        var previousEmail = contact.Email;
        if (request.Data is not null)
        {
            var (email, data) = ContactData.Clean(request.Data);
            if (email.Length == 0)
            {
                return ContactErrors.EmailRequiredOnEdit;
            }

            if (email != previousEmail
                && await db.Contacts.AnyAsync(c => c.ListId == contact.ListId && c.Email == email && c.Id != contactId, ct))
            {
                return ContactErrors.DuplicateOnEdit;
            }

            contact.Email = email;
            contact.Data = data;
            var list = await db.ContactLists.SingleAsync(l => l.Id == contact.ListId, ct);
            list.Columns = ContactData.MergeColumns(list.Columns, data.Keys);
        }

        if (request.DoNotEmail is { } doNotEmail)
        {
            contact.DoNotEmail = doNotEmail;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        if (contact.Email != previousEmail)
        {
            await RepointUnsentMessages(contact, ct);
        }

        await tx.CommitAsync(ct);
        return ContactView.From(contact, await optOuts.LoadAsync(user.WorkspaceId, ct));
    }

    /// <summary>
    /// A fixed typo should reach the corrected address: campaign rows not yet emailed follow the contact,
    /// unless that campaign already has a row for the new address.
    /// </summary>
    private Task<int> RepointUnsentMessages(Contact contact, CancellationToken ct) =>
        db.Messages
            .Where(m => m.ContactId == contact.Id
                        && (m.Status == MessageStatus.Pending || m.Status == MessageStatus.Skipped)
                        && !db.Messages.Any(o => o.CampaignId == m.CampaignId && o.Email == contact.Email))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Email, contact.Email), ct);
}
