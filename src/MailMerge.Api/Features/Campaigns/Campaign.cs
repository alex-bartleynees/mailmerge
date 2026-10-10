using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Lists;
using MailMerge.Api.Shared.Persistence;
using MailMerge.Api.Shared.Merging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Campaigns;

public enum CampaignMode
{
    /// <summary>Create drafts in the mailbox for staff to review and send themselves.</summary>
    Draft,

    /// <summary>Create each draft and send it; it lands in Sent Items.</summary>
    Send,
}

public enum CampaignStatus
{
    Idle,
    Running,
    Paused,
    Done,
}

/// <summary>One email (subject + body with merge fields) sent to one contact list from one account.</summary>
public sealed class Campaign : IWorkspaceOwned
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public required string Name { get; set; }
    public long? AccountId { get; set; }
    public long? ListId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsHtml { get; set; }
    public CampaignMode Mode { get; set; } = CampaignMode.Draft;
    public CampaignStatus Status { get; set; } = CampaignStatus.Idle;

    /// <summary>Why the campaign paused, in words staff can act on.</summary>
    public string? Note { get; set; }

    /// <summary>Append an unsubscribe link to every message.</summary>
    public bool UnsubscribeFooter { get; set; }

    /// <summary>Plain text shown before the unsubscribe link. Null uses the default wording.</summary>
    public string? UnsubscribeMessage { get; set; }

    public bool IncludeBusinessFooter { get; set; }
    public bool UseWorkspaceBusinessFooter { get; set; } = true;
    public BusinessFooter? BusinessFooter { get; set; }

    /// <summary>Only contacts matching this are emailed; the rest are skipped. Null means everyone on the list.</summary>
    public RecipientFilter? Filter { get; set; }

    public DateTimeOffset Created { get; set; }

    /// <summary>Last run/pause/send/mark. The retention clock starts here.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>Set when retention purged the per-recipient rows. Archived campaigns can't run again.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>Final status counts, kept after the per-recipient rows are purged.</summary>
    public Dictionary<string, int>? Summary { get; set; }

    public bool IsArchived => ArchivedAt is not null;
}

internal sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> b)
    {
        b.HasIndex(c => c.WorkspaceId);
        b.HasIndex(c => c.Status);
        b.Property(c => c.Name).HasMaxLength(200);
        b.Property(c => c.Subject).HasMaxLength(998);
        b.Property(c => c.Mode).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(c => c.Note).HasMaxLength(1000);
        b.Property(c => c.UnsubscribeMessage).HasMaxLength(1000);
        b.Property(c => c.Summary).HasJsonConversion();
        b.Property(c => c.Filter).HasJsonConversion();
        b.Property(c => c.BusinessFooter).HasJsonConversion();
        b.HasOne<SendingAccount>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ContactList>().WithMany().HasForeignKey(c => c.ListId).OnDelete(DeleteBehavior.SetNull);
    }
}
