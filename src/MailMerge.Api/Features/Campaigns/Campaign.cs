using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Lists;
using MailMerge.Api.Shared.Persistence;
using SharedKernel.Results;
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

/// <summary>
/// One email (subject + body with merge fields) sent to one contact list from one account. The aggregate root for
/// the email's lifecycle: content is edited freely through its properties, but status, mode, archive state and
/// the retention clock only change through the methods below, which hold the rules (can't edit while sending,
/// archived emails never run again, what retention may purge). <see cref="Version"/> is Postgres's xmin, so a
/// handler's save fails cleanly if the delivery worker changed the row meanwhile.
/// </summary>
public sealed class Campaign : IWorkspaceOwned
{
    private Campaign()
    {
    }

    public static Campaign Create(long workspaceId, string name, DateTimeOffset now) => new()
    {
        WorkspaceId = workspaceId,
        Name = name,
        Created = now,
        LastActivityAt = now,
    };

    /// <summary>
    /// "Copy as new email": the same content, recipients, filter and footers with no send history, idle and editable,
    /// even when this one is archived. Add new content fields here so copies keep them.
    /// </summary>
    public Campaign CopyAsNew(DateTimeOffset now)
    {
        var copy = Create(WorkspaceId, Name.Length > 190 ? Name[..190] + " (copy)" : Name + " (copy)", now);
        copy.AccountId = AccountId;
        copy.ListId = ListId;
        copy.Subject = Subject;
        copy.Body = Body;
        copy.IsHtml = IsHtml;
        copy.UnsubscribeFooter = UnsubscribeFooter;
        copy.UnsubscribeMessage = UnsubscribeMessage;
        copy.IncludeBusinessFooter = IncludeBusinessFooter;
        copy.UseWorkspaceBusinessFooter = UseWorkspaceBusinessFooter;
        copy.BusinessFooter = BusinessFooter;
        copy.Filter = Filter;
        return copy;
    }

    public long Id { get; private set; }
    public long WorkspaceId { get; set; }
    public string Name { get; set; } = "";
    public long? AccountId { get; set; }
    public long? ListId { get; set; }
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public bool IsHtml { get; set; }
    public CampaignMode Mode { get; private set; } = CampaignMode.Draft;
    public CampaignStatus Status { get; private set; } = CampaignStatus.Idle;

    /// <summary>Why the campaign paused, in words staff can act on.</summary>
    public string? Note { get; private set; }

    /// <summary>Append an unsubscribe link to every message.</summary>
    public bool UnsubscribeFooter { get; set; }

    /// <summary>Plain text shown before the unsubscribe link. Null uses the default wording.</summary>
    public string? UnsubscribeMessage { get; set; }

    public bool IncludeBusinessFooter { get; set; }
    public bool UseWorkspaceBusinessFooter { get; set; } = true;
    public BusinessFooter? BusinessFooter { get; set; }

    /// <summary>Only contacts matching this are emailed; the rest are skipped. Null means everyone on the list.</summary>
    public RecipientFilter? Filter { get; set; }

    public DateTimeOffset Created { get; private set; }

    /// <summary>Last run/pause/send/mark. The retention clock starts here.</summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>Set when retention purged the per-recipient rows. Archived campaigns can't run again.</summary>
    public DateTimeOffset? ArchivedAt { get; private set; }

    /// <summary>Final status counts, kept after the per-recipient rows are purged.</summary>
    public Dictionary<string, int>? Summary { get; private set; }

    /// <summary>Optimistic-concurrency token (Postgres xmin).</summary>
    public uint Version { get; private set; }

    public bool IsArchived => ArchivedAt is not null;

    public bool IsRunning => Status == CampaignStatus.Running;

    /// <summary>Content, recipients and attachments can only change while the email isn't sending or archived.</summary>
    public Result EnsureEditable() =>
        IsRunning ? CampaignErrors.RunningEdit
        : IsArchived ? CampaignErrors.Archived
        : Result.Success();

    public Result EnsureDeletable() => IsRunning ? CampaignErrors.RunningDelete : Result.Success();

    /// <summary>Failed rows can be queued again unless the history has been archived.</summary>
    public Result EnsureCanRequeue() => IsArchived ? CampaignErrors.Archived : Result.Success();

    /// <summary>
    /// Everything a run needs: not already running or archived, an account (that can save drafts, for draft mode),
    /// a list, a subject and a message. Changes nothing.
    /// </summary>
    public Result CanStart(CampaignMode mode, SendingAccount? account)
    {
        var error = this switch
        {
            { IsRunning: true } => CampaignErrors.AlreadyRunning,
            { IsArchived: true } => CampaignErrors.Archived,
            _ when account is null => CampaignErrors.NoAccount,
            { ListId: null } => CampaignErrors.NoList,
            _ when string.IsNullOrWhiteSpace(Subject) => CampaignErrors.NoSubject,
            _ when string.IsNullOrWhiteSpace(Body) => CampaignErrors.NoBody,
            _ when mode == CampaignMode.Draft && !account!.SupportsDrafts => CampaignErrors.NoDrafts,
            _ => null,
        };
        return error is null ? Result.Success() : error;
    }

    /// <summary>Starts (or resumes) sending in <paramref name="mode"/> if <see cref="CanStart"/> allows it.</summary>
    public Result Start(CampaignMode mode, SendingAccount? account, DateTimeOffset now)
    {
        var allowed = CanStart(mode, account);
        if (allowed.IsFailure)
        {
            return allowed;
        }

        Status = CampaignStatus.Running;
        Mode = mode;
        Note = null;
        LastActivityAt = now;
        return Result.Success();
    }

    /// <summary>Stops after the message in flight. Returns false (and changes nothing) if it wasn't running.</summary>
    public bool Pause(DateTimeOffset now)
    {
        if (!IsRunning)
        {
            return false;
        }

        Status = CampaignStatus.Paused;
        Note = null;
        LastActivityAt = now;
        return true;
    }

    /// <summary>
    /// When retention will archive this email: <see cref="LastActivityAt"/> + the retention period. Null while it's
    /// sending, already archived, or has messages awaiting a human decision (uncertain), which are never purged.
    /// </summary>
    public DateTimeOffset? PurgesOn(int retentionDays, bool hasMessagesAwaitingDecision) =>
        IsArchived || IsRunning || hasMessagesAwaitingDecision ? null : LastActivityAt.AddDays(retentionDays);

    public bool CanBeArchived(DateTimeOffset now, int retentionDays, bool hasMessagesAwaitingDecision) =>
        PurgesOn(retentionDays, hasMessagesAwaitingDecision) is { } due && due < now;

    /// <summary>Retention: keep the email and its totals; the caller deletes the per-recipient rows and attachments.</summary>
    public void Archive(DateTimeOffset now, Dictionary<string, int> summary)
    {
        ArchivedAt = now;
        Summary = summary;
        Status = CampaignStatus.Done;
        Note = null;
    }
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
        b.Property(c => c.Version).IsRowVersion();
        b.Property(c => c.Filter).HasJsonConversion();
        b.Property(c => c.BusinessFooter).HasJsonConversion();
        b.HasOne<SendingAccount>().WithMany().HasForeignKey(c => c.AccountId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ContactList>().WithMany().HasForeignKey(c => c.ListId).OnDelete(DeleteBehavior.SetNull);
    }
}
