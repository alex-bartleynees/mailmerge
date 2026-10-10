using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Campaigns;

namespace MailMerge.UnitTests.Campaigns;

/// <summary>The email lifecycle rules, tested without a database.</summary>
public sealed class CampaignAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly SendingAccount Outlook = new() { Name = "Outlook", Kind = AccountKind.Microsoft };
    private static readonly SendingAccount SmtpOnly = new() { Name = "SMTP", Kind = AccountKind.Smtp, Smtp = new SmtpSettings { Host = "h" } };

    private static Campaign Ready()
    {
        var campaign = Campaign.Create(1, "Spring", Now.AddDays(-1));
        campaign.ListId = 7;
        campaign.Subject = "Hello";
        campaign.Body = "Hi";
        return campaign;
    }

    [Fact]
    public void Start_runs_a_complete_email_and_records_the_mode()
    {
        var campaign = Ready();

        campaign.Start(CampaignMode.Send, Outlook, Now).IsSuccess.Should().BeTrue();

        campaign.Status.Should().Be(CampaignStatus.Running);
        campaign.Mode.Should().Be(CampaignMode.Send);
        campaign.LastActivityAt.Should().Be(Now);
    }

    [Fact]
    public void Start_explains_what_is_missing_in_order()
    {
        var campaign = Campaign.Create(1, "x", Now);

        campaign.CanStart(CampaignMode.Send, null).Error.Should().Be(CampaignErrors.NoAccount);
        campaign.CanStart(CampaignMode.Send, Outlook).Error.Should().Be(CampaignErrors.NoList);
        campaign.ListId = 7;
        campaign.CanStart(CampaignMode.Send, Outlook).Error.Should().Be(CampaignErrors.NoSubject);
        campaign.Subject = "S";
        campaign.CanStart(CampaignMode.Send, Outlook).Error.Should().Be(CampaignErrors.NoBody);
        campaign.Body = "B";
        campaign.CanStart(CampaignMode.Draft, SmtpOnly).Error.Should().Be(CampaignErrors.NoDrafts);
        campaign.CanStart(CampaignMode.Send, SmtpOnly).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void A_failed_start_changes_nothing()
    {
        var campaign = Campaign.Create(1, "x", Now.AddDays(-1));

        campaign.Start(CampaignMode.Send, Outlook, Now).IsFailure.Should().BeTrue();

        campaign.Status.Should().Be(CampaignStatus.Idle);
        campaign.LastActivityAt.Should().Be(Now.AddDays(-1));
    }

    [Fact]
    public void A_running_email_cannot_be_edited_deleted_or_started_again()
    {
        var campaign = Ready();
        campaign.Start(CampaignMode.Send, Outlook, Now);

        campaign.EnsureEditable().Error.Should().Be(CampaignErrors.RunningEdit);
        campaign.EnsureDeletable().Error.Should().Be(CampaignErrors.RunningDelete);
        campaign.CanStart(CampaignMode.Send, Outlook).Error.Should().Be(CampaignErrors.AlreadyRunning);
    }

    [Fact]
    public void Pause_only_affects_a_running_email()
    {
        var campaign = Ready();
        campaign.Pause(Now).Should().BeFalse();
        campaign.Status.Should().Be(CampaignStatus.Idle);

        campaign.Start(CampaignMode.Send, Outlook, Now);
        campaign.Pause(Now.AddMinutes(1)).Should().BeTrue();

        campaign.Status.Should().Be(CampaignStatus.Paused);
        campaign.EnsureEditable().IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Purge_date_is_last_activity_plus_retention_unless_sending_or_awaiting_a_decision()
    {
        var campaign = Ready();

        campaign.PurgesOn(90, hasMessagesAwaitingDecision: false).Should().Be(Now.AddDays(89));
        campaign.PurgesOn(90, hasMessagesAwaitingDecision: true).Should().BeNull();
        campaign.CanBeArchived(Now.AddDays(90), 90, false).Should().BeTrue();
        campaign.CanBeArchived(Now.AddDays(10), 90, false).Should().BeFalse();
        campaign.CanBeArchived(Now.AddDays(400), 90, true).Should().BeFalse("someone must check Sent Items first");

        campaign.Start(CampaignMode.Send, Outlook, Now);
        campaign.PurgesOn(90, false).Should().BeNull();
    }

    [Fact]
    public void An_archived_email_keeps_its_totals_and_can_only_be_copied()
    {
        var campaign = Ready();
        campaign.Filter = new RecipientFilter(FilterMatch.All, [new FilterCondition("RSVP", FilterOperator.IsYes)]);

        campaign.Archive(Now, new Dictionary<string, int> { ["sent"] = 3, ["total"] = 3 });

        campaign.Summary!["sent"].Should().Be(3);
        campaign.EnsureEditable().Error.Should().Be(CampaignErrors.Archived);
        campaign.EnsureCanRequeue().Error.Should().Be(CampaignErrors.Archived);
        campaign.CanStart(CampaignMode.Send, Outlook).Error.Should().Be(CampaignErrors.Archived);
        campaign.PurgesOn(90, false).Should().BeNull();

        var copy = campaign.CopyAsNew(Now.AddDays(1));
        copy.Name.Should().Be("Spring (copy)");
        copy.Status.Should().Be(CampaignStatus.Idle);
        copy.IsArchived.Should().BeFalse();
        copy.Summary.Should().BeNull();
        copy.Subject.Should().Be("Hello");
        copy.Filter.Should().Be(campaign.Filter);
        copy.EnsureEditable().IsSuccess.Should().BeTrue();
    }
}

public sealed class MessageAggregateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_messages_are_pending_or_skipped_with_the_reason()
    {
        Message.Create(1, 2, 3, "a@example.com", null, Now).Status.Should().Be(MessageStatus.Pending);
        var skipped = Message.Create(1, 2, 3, "a@example.com", "On the opt-out list", Now);
        skipped.Status.Should().Be(MessageStatus.Skipped);
        skipped.Error.Should().Be("On the opt-out list");
    }

    [Fact]
    public void Reevaluate_only_rejudges_skipped_rows()
    {
        var skipped = Message.Create(1, 2, 3, "a@example.com", "Invalid email address", Now);
        skipped.Reevaluate(4, null, Now.AddHours(1));
        skipped.Status.Should().Be(MessageStatus.Pending);
        skipped.ContactId.Should().Be(4);

        var marked = Message.Create(1, 2, 3, "b@example.com", null, Now);
        marked.Mark(MessageStatus.Sent, Now);
        marked.Reevaluate(5, "On the opt-out list", Now.AddHours(1));
        marked.Status.Should().Be(MessageStatus.Sent, "a sent message stays sent on re-run");
    }

    [Fact]
    public void Staff_can_resolve_unresolved_messages_once()
    {
        var message = Message.Create(1, 2, 3, "a@example.com", null, Now);

        message.Mark(MessageStatus.Failed, Now).Error.Should().Be(CampaignErrors.MessageOutcome);
        message.Mark(MessageStatus.Sent, Now).IsSuccess.Should().BeTrue();
        message.Error.Should().Be("Marked by you");
        message.Mark(MessageStatus.Pending, Now).Error.Should().Be(CampaignErrors.MessageNotUnresolved);
    }

    [Fact]
    public void Requeue_only_touches_failed_messages() =>
        Message.Create(1, 2, 3, "a@example.com", null, Now).Requeue(Now).Should().BeFalse();
}
