using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Shared.Persistence;
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

/// <summary>One recipient of one campaign. Unique per (campaign, email) so re-runs only reach new people.</summary>
public sealed class Message : IWorkspaceOwned
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public long CampaignId { get; set; }
    public long? ContactId { get; set; }
    public required string Email { get; set; }
    public MessageStatus Status { get; set; }
    public string? Error { get; set; }

    /// <summary>Draft/message id in the mailbox (Graph message id, IMAP UID, or test file name).</summary>
    public string? ProviderId { get; set; }

    public DateTimeOffset Updated { get; set; }
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
        b.HasOne<Campaign>().WithMany().HasForeignKey(m => m.CampaignId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Contact>().WithMany().HasForeignKey(m => m.ContactId).OnDelete(DeleteBehavior.SetNull);
    }
}
