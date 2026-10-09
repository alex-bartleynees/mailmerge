using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Campaigns.Delivery;

/// <summary>Who gets a campaign and who is skipped, and the per-recipient rows that make re-runs safe.</summary>
public sealed class DeliveryPlanner(MailMergeDbContext db, OptOutRegistry optOuts, TimeProvider clock)
{
    /// <summary>Null if the contact can be emailed, otherwise the reason shown to staff.</summary>
    public static string? SkipReason(Contact contact, OptOutMatcher optedOut)
    {
        if (!EmailAddress.IsValid(contact.Email))
        {
            return "Invalid email address";
        }

        if (contact.DoNotEmail)
        {
            return "Marked do-not-email";
        }

        return optedOut.IsOptedOut(contact.Email) ? "On the opt-out list" : null;
    }

    /// <summary>
    /// Creates or refreshes one message row per contact. Rows already drafted, sent, failed or uncertain keep their
    /// status, so re-running after adding contacts only reaches the new people; skipped rows are re-evaluated.
    /// </summary>
    public async Task MaterializeAsync(Campaign campaign, CancellationToken ct)
    {
        var contacts = await ContactsAsync(campaign, ct);
        var matcher = await optOuts.LoadAsync(campaign.WorkspaceId, ct);
        var existing = await db.Messages.Where(m => m.CampaignId == campaign.Id).ToDictionaryAsync(m => m.Email, ct);
        var now = clock.GetUtcNow();

        foreach (var contact in contacts)
        {
            var reason = SkipReason(contact, matcher);
            var status = reason is null ? MessageStatus.Pending : MessageStatus.Skipped;
            if (existing.TryGetValue(contact.Email, out var message))
            {
                message.ContactId = contact.Id;
                if (message.Status == MessageStatus.Skipped)
                {
                    message.Status = status;
                    message.Error = reason;
                    message.Updated = now;
                }

                continue;
            }

            var created = new Message
            {
                WorkspaceId = campaign.WorkspaceId,
                CampaignId = campaign.Id,
                ContactId = contact.Id,
                Email = contact.Email,
                Status = status,
                Error = reason,
                Updated = now,
            };
            db.Messages.Add(created);
            existing[contact.Email] = created;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task<DeliveryPlan> PlanAsync(Campaign campaign, CancellationToken ct)
    {
        if (campaign.ListId is null)
        {
            return new DeliveryPlan(0, 0, 0, 0, 0, 0, 0);
        }

        var contacts = await ContactsAsync(campaign, ct);
        var matcher = await optOuts.LoadAsync(campaign.WorkspaceId, ct);
        var statuses = await db.Messages.AsNoTracking().Where(m => m.CampaignId == campaign.Id)
            .ToDictionaryAsync(m => m.Email, m => m.Status, ct);

        int skipped = 0, toDraft = 0, toSend = 0, sent = 0, failed = 0, uncertain = 0;
        foreach (var contact in contacts)
        {
            if (SkipReason(contact, matcher) is not null)
            {
                skipped++;
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

        return new DeliveryPlan(contacts.Count, skipped, toDraft, toSend, sent, failed, uncertain);
    }

    private Task<List<Contact>> ContactsAsync(Campaign campaign, CancellationToken ct) =>
        db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.WorkspaceId == campaign.WorkspaceId && c.ListId == campaign.ListId)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
}
