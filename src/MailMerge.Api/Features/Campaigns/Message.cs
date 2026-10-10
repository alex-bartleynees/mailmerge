using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Shared.Persistence;
using SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Campaigns;

public enum MessageStatus
{
    /// <summary>Waiting to be drafted.</summary>
    Pending,

    /// <summary>Draft being created right now. Reset to Pending on restart (the draft id isn't saved yet).</summary>
    Drafting,

    /// <summary>Draft exists in the mailbox; <see cref="Message.ProviderId"/> holds its id.</summary>
    Drafted,

    /// <summary>The irreversible send call is in flight. Becomes Uncertain on restart, never retried automatically.</summary>
    Sending,

    Sent,
    Failed,

    /// <summary>Not emailed: invalid address, "don't email", or opted out.</summary>
    Skipped,

    /// <summary>We can't tell whether it went out. Staff check Sent Items, then mark it sent or retry it.</summary>
    Uncertain,
}

/// <summary>
/// One recipient of one campaign. Unique per (campaign, email) so re-runs only reach new people. A small aggregate of
/// its own: staff decisions go through <see cref="Mark"/> and <see cref="Requeue"/>; the delivery worker moves
/// messages through drafting/sending with conditional SQL updates (its no-double-send guarantee), which also bump
/// <see cref="Version"/>, so a staff save that races the worker fails instead of overwriting it.
/// </summary>
public sealed class Message : IWorkspaceOwned
{
    /// <summary>Statuses staff may resolve by hand.</summary>
    public static readonly IReadOnlySet<MessageStatus> Unresolved = new HashSet<MessageStatus>
        { MessageStatus.Uncertain, MessageStatus.Failed, MessageStatus.Skipped, MessageStatus.Pending };

    /// <summary>Statuses that need a person (or the worker) before the history may be purged.</summary>
    public static readonly IReadOnlySet<MessageStatus> AwaitingDecision = new HashSet<MessageStatus>
        { MessageStatus.Uncertain, MessageStatus.Drafting, MessageStatus.Sending };

    private Message()
    {
    }

    /// <summary>A new recipient row: pending, or skipped with <paramref name="skipReason"/>.</summary>
    public static Message Create(long workspaceId, long campaignId, long contactId, string email, string? skipReason,
        DateTimeOffset now) => new()
    {
        WorkspaceId = workspaceId,
        CampaignId = campaignId,
        ContactId = contactId,
        Email = email,
        Status = skipReason is null ? MessageStatus.Pending : MessageStatus.Skipped,
        Error = skipReason,
        Updated = now,
    };

    public long Id { get; private set; }
    public long WorkspaceId { get; set; }
    public long CampaignId { get; private set; }
    public long? ContactId { get; private set; }
    public string Email { get; private set; } = "";
    public MessageStatus Status { get; private set; }
    public string? Error { get; private set; }

    /// <summary>Draft/message id in the mailbox (Graph message id, IMAP UID, or test file name).</summary>
    public string? ProviderId { get; private set; }

    public DateTimeOffset Updated { get; private set; }

    /// <summary>Optimistic-concurrency token (Postgres xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>
    /// A re-run refreshes the contact link; a skipped row is re-judged (e.g. the address was fixed or the filter
    /// changed). Rows already drafted, sent, failed or uncertain keep their status.
    /// </summary>
    public void Reevaluate(long contactId, string? skipReason, DateTimeOffset now)
    {
        ContactId = contactId;
        if (Status != MessageStatus.Skipped)
        {
            return;
        }

        Status = skipReason is null ? MessageStatus.Pending : MessageStatus.Skipped;
        Error = skipReason;
        Updated = now;
    }

    /// <summary>
    /// Staff resolve a message: <c>Sent</c> ("I checked Sent Items, it went"), <c>Pending</c> ("retry it") or
    /// <c>Skipped</c> ("don't send it"). Only unresolved messages can be changed.
    /// </summary>
    public Result Mark(MessageStatus outcome, DateTimeOffset now)
    {
        if (outcome is not (MessageStatus.Sent or MessageStatus.Pending or MessageStatus.Skipped))
        {
            return CampaignErrors.MessageOutcome;
        }

        if (!Unresolved.Contains(Status))
        {
            return CampaignErrors.MessageNotUnresolved;
        }

        Status = outcome;
        Error = outcome == MessageStatus.Pending ? null : "Marked by you";
        Updated = now;
        return Result.Success();
    }

    /// <summary>A failed message goes back to waiting. Anything else (notably uncertain) is left alone.</summary>
    public bool Requeue(DateTimeOffset now)
    {
        if (Status != MessageStatus.Failed)
        {
            return false;
        }

        Status = MessageStatus.Pending;
        Error = null;
        Updated = now;
        return true;
    }
}

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> b)
    {
        b.HasIndex(m => new { m.CampaignId, m.Email }).IsUnique();
        b.HasIndex(m => new { m.CampaignId, m.Status });
        b.HasIndex(m => new { m.WorkspaceId, m.Email });
        b.Property(m => m.Email).HasMaxLength(320);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(m => m.Error).HasMaxLength(1000);
        b.Property(m => m.ProviderId).HasMaxLength(500);
        b.Property(m => m.Version).IsRowVersion();
        b.HasOne<Campaign>().WithMany().HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Contact>().WithMany().HasForeignKey(m => m.ContactId).OnDelete(DeleteBehavior.SetNull);
    }
}
