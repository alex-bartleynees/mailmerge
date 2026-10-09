using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Privacy;

/// <summary>Everything stored about one email address in the current workspace.</summary>
public sealed record PersonRecords(
    string Email,
    IReadOnlyList<PersonRecords.ListEntry> Lists,
    IReadOnlyList<PersonRecords.CampaignEntry> Campaigns,
    bool OptedOut,
    int PendingImports)
{
    public sealed record ListEntry(long ListId, string ListName, long ContactId);

    /// <summary><c>DraftInMailbox</c>: an unsent draft for this person sits in the account's Drafts folder.</summary>
    public sealed record CampaignEntry(long CampaignId, string CampaignName, MessageStatus Status, bool DraftInMailbox, string? Account);

    public bool HasAnything => Lists.Count > 0 || Campaigns.Count > 0 || OptedOut;

    public static async Task<PersonRecords> FindAsync(MailMergeDbContext db, string emailHash, string email, CancellationToken ct)
    {
        email = EmailAddress.Normalize(email);
        var lists = await db.Contacts.AsNoTracking().Where(c => c.Email == email)
            .Join(db.ContactLists, c => c.ListId, l => l.Id, (c, l) => new ListEntry(l.Id, l.Name, c.Id))
            .ToListAsync(ct);
        var campaigns = await db.Messages.AsNoTracking().Where(m => m.Email == email)
            .Join(db.Campaigns, m => m.CampaignId, c => c.Id, (m, c) => new { m, c })
            .Select(x => new CampaignEntry(x.c.Id, x.c.Name, x.m.Status,
                x.m.Status == MessageStatus.Drafted && x.m.ProviderId != null,
                db.SendingAccounts.Where(a => a.Id == x.c.AccountId).Select(a => a.Name).FirstOrDefault()))
            .ToListAsync(ct);
        var optedOut = await db.OptOuts.AnyAsync(o => o.EmailHash == emailHash, ct);
        // Spreadsheets waiting to be imported may contain the address; they're opaque bytes, so count them all.
        var imports = await db.ImportUploads.CountAsync(ct);
        return new PersonRecords(email, lists, campaigns, optedOut, imports);
    }
}
