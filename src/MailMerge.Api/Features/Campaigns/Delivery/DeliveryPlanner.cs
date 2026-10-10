using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Delivery;

/// <summary>Who gets a campaign and who is skipped, and the per-recipient rows that make re-runs safe.</summary>
public sealed class DeliveryPlanner(MailMergeDbContext db, OptOutRegistry optOuts, TimeProvider clock)
{
    public async Task<Error?> FilterErrorAsync(Campaign campaign, CancellationToken ct)
    {
        if (campaign.Filter is null || campaign.Filter.IsEmpty)
        {
            return null;
        }

        var columns = await db.ContactLists.IgnoreQueryFilters().AsNoTracking()
            .Where(l => l.Id == campaign.ListId && l.WorkspaceId == campaign.WorkspaceId)
            .Select(l => l.Columns).SingleOrDefaultAsync(ct) ?? [];
        return campaign.Filter.ValidateColumns(columns);
    }

    /// <summary>Null if the contact can be emailed, otherwise the reason shown to staff.</summary>
    public const string FilteredOutReason = "Not included by this email's filter";

    public static string? SkipReason(Contact contact, bool optedOut, RecipientFilter? filter = null)
    {
        if (!EmailAddress.IsValid(contact.Email))
        {
            return "Invalid email address";
        }

        if (contact.DoNotEmail)
        {
            return "Marked do-not-email";
        }

        if (optedOut)
        {
            return "On the opt-out list";
        }

        return filter is not null && !filter.Matches(contact.MergeData()) ? FilteredOutReason : null;
    }

    /// <summary>
    /// Creates or refreshes one message row per contact. Rows already drafted, sent, failed or uncertain keep their
    /// status, so re-running after adding contacts only reaches the new people; skipped rows are re-evaluated.
    /// Only tracks the changes: the caller saves them together with the campaign, in one conflict-checked save.
    /// </summary>
    public async Task MaterializeAsync(Campaign campaign, CancellationToken ct)
    {
        var contacts = await ContactsAsync(campaign, ct);
        var matcher = await optOuts.LoadAsync(campaign.WorkspaceId, ct);
        var existing = await db.Messages.Where(m => m.CampaignId == campaign.Id).ToDictionaryAsync(m => m.Email, ct);
        var now = clock.GetUtcNow();

        foreach (var contact in contacts)
        {
            var reason = SkipReason(contact, matcher.IsOptedOut(contact.Email), campaign.Filter);
            if (existing.TryGetValue(contact.Email, out var message))
            {
                message.Reevaluate(contact.Id, reason, now);
                continue;
            }

            var created = Message.Create(campaign.WorkspaceId, campaign.Id, contact.Id, contact.Email, reason, now);
            db.Messages.Add(created);
            existing[contact.Email] = created;
        }
    }

    public async Task<DeliveryPlan> PlanAsync(Campaign campaign, CancellationToken ct)
    {
        if (campaign.ListId is null)
        {
            return new DeliveryPlan(0, 0, 0, 0, 0, 0, 0, 0);
        }

        var contacts = await ContactsAsync(campaign, ct);
        var matcher = await optOuts.LoadAsync(campaign.WorkspaceId, ct);
        var statuses = await db.Messages.AsNoTracking().Where(m => m.CampaignId == campaign.Id)
            .ToDictionaryAsync(m => m.Email, m => m.Status, ct);

        if (await FilterErrorAsync(campaign, ct) is not null)
        {
            return new DeliveryPlan(contacts.Count, contacts.Count, contacts.Count, 0, 0, 0, 0, 0);
        }

        int skipped = 0, filteredOut = 0, toDraft = 0, toSend = 0, sent = 0, failed = 0, uncertain = 0;
        foreach (var contact in contacts)
        {
            if (SkipReason(contact, matcher.IsOptedOut(contact.Email), campaign.Filter) is { } reason)
            {
                skipped++;
                if (reason == FilteredOutReason)
                {
                    filteredOut++;
                }

                continue;
            }

            switch (statuses.TryGetValue(contact.Email, out var s) ? s : (MessageStatus?)null)
            {
                case null or MessageStatus.Pending or MessageStatus.Skipped:
                    toDraft++;
                    toSend++;
                    break;
                case MessageStatus.Drafted or MessageStatus.Drafting:
                    toSend++;
                    break;
                case MessageStatus.Sent:
                    sent++;
                    break;
                case MessageStatus.Failed:
                    failed++;
                    break;
                case MessageStatus.Uncertain or MessageStatus.Sending:
                    uncertain++;
                    break;
            }
        }

        return new DeliveryPlan(contacts.Count, skipped, filteredOut, toDraft, toSend, sent, failed, uncertain);
    }

    private Task<List<Contact>> ContactsAsync(Campaign campaign, CancellationToken ct) =>
        db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.WorkspaceId == campaign.WorkspaceId && c.ListId == campaign.ListId)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
}
