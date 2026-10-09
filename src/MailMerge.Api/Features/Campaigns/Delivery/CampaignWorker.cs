using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Features.OptOuts.Unsubscribe;
using MailMerge.Api.Host;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Campaigns.Delivery;

/// <summary>
/// Sends one message at a time, pacing each sending account separately.
/// <para>Safety rules:</para>
/// <list type="bullet">
/// <item>A message is moved to Drafting/Sending <em>before</em> the network call. After a crash, Drafting goes back to
/// Pending and Sending becomes Uncertain: never re-sent automatically.</item>
/// <item>The draft id is saved the moment the draft exists, so a retry sends that draft instead of making another.</item>
/// <item>Account problems (signed out, wrong password) pause the campaign instead of failing every row.</item>
/// <item><see cref="MaxFailureStreak"/> failures in a row pause the campaign.</item>
/// <item>Claims are conditional updates, so a second instance can't process the same message.</item>
/// </list>
/// Runs as a single instance; startup recovery assumes no other worker is mid-send.
/// </summary>
public sealed class CampaignWorker(
    IServiceScopeFactory scopes,
    IMailSenderFactory senders,
    UnsubscribeLinks unsubscribeLinks,
    DeliverySignal signal,
    TimeProvider clock,
    ILogger<CampaignWorker> log) : BackgroundService
{
    public const int MaxFailureStreak = 5;

    public const string InterruptedSend =
        "The app stopped while sending this. Check Sent Items, then mark it as sent or retry it.";

    private static readonly TimeSpan IdlePoll = TimeSpan.FromSeconds(2);

    private readonly Dictionary<long, int> _streaks = new();
    private readonly Dictionary<long, IReadOnlyList<OutgoingAttachment>> _attachments = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;
            try
            {
                wait = await TickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e)
            {
                log.LogError(e, "Delivery worker error");
                wait = TimeSpan.FromSeconds(3);
            }

            if (wait > TimeSpan.Zero)
            {
                try
                {
                    await signal.WaitAsync(wait, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Resolves messages left mid-flight by a previous process.</summary>
    internal async Task RecoverAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MailMergeDbContext>();
        var now = clock.GetUtcNow();
        var messages = db.Messages.IgnoreQueryFilters();

        // No draft id was saved, so there's nothing to reuse: start this one again.
        await messages.Where(m => m.Status == MessageStatus.Drafting).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, MessageStatus.Pending)
            .SetProperty(m => m.Error, (string?)null)
            .SetProperty(m => m.Updated, now), ct);
        var interrupted = await messages.Where(m => m.Status == MessageStatus.Sending).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Status, MessageStatus.Uncertain)
            .SetProperty(m => m.Error, InterruptedSend)
            .SetProperty(m => m.Updated, now), ct);
        if (interrupted > 0)
        {
            log.LogWarning("{Count} message(s) were mid-send when the app stopped; marked uncertain", interrupted);
        }
    }

    /// <summary>Processes at most one message. Returns how long to wait before the next tick.</summary>
    internal async Task<TimeSpan> TickAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MailMergeDbContext>();
        var campaigns = db.Campaigns.IgnoreQueryFilters();
        var now = clock.GetUtcNow();

        await campaigns.Where(c => c.Status == CampaignStatus.Running && c.AccountId == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, CampaignStatus.Paused)
                .SetProperty(c => c.Note, "The sending account was removed. Choose another account and press Resume.")
                .SetProperty(c => c.LastActivityAt, now), ct);

        var accounts = await db.SendingAccounts.IgnoreQueryFilters()
            .Where(a => campaigns.Any(c => c.AccountId == a.Id && c.Status == CampaignStatus.Running))
            .Select(a => new { a.Id, a.NextSendAt })
            .ToListAsync(ct);
        if (accounts.Count == 0)
        {
            _attachments.Clear();
            return IdlePoll;
        }

        var due = accounts.Where(a => a.NextSendAt is null || a.NextSendAt <= now)
            .OrderBy(a => a.NextSendAt ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
        if (due is null)
        {
            var soonest = accounts.Min(a => a.NextSendAt!.Value) - now;
            return soonest < IdlePoll ? soonest : IdlePoll;
        }

        var account = await db.SendingAccounts.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == due.Id, ct);
        var campaign = await campaigns.AsNoTracking()
            .Where(c => c.AccountId == account.Id && c.Status == CampaignStatus.Running)
            .OrderBy(c => c.Id)
            .FirstAsync(ct);

        var wanted = campaign.Mode == CampaignMode.Send
            ? new[] { MessageStatus.Pending, MessageStatus.Drafted }
            : [MessageStatus.Pending];
        var message = await db.Messages.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.CampaignId == campaign.Id && wanted.Contains(m.Status))
            .OrderBy(m => m.Id)
            .FirstOrDefaultAsync(ct);
        if (message is null)
        {
            await FinishAsync(db, campaign.Id, CampaignStatus.Done, note: null, ct);
            return TimeSpan.Zero;
        }

        if (await ProcessAsync(scope.ServiceProvider, db, account, campaign, message, ct))
        {
            var next = Pacing.NextSendAt(clock.GetUtcNow(), account.PerMinute, Random.Shared.NextDouble());
            await db.SendingAccounts.IgnoreQueryFilters().Where(a => a.Id == account.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.NextSendAt, next), ct);
        }

        await campaigns.Where(c => c.Id == campaign.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, clock.GetUtcNow()), ct);
        return TimeSpan.Zero;
    }

    /// <summary>Returns true when the mail server was contacted (so the account's pacing applies).</summary>
    private async Task<bool> ProcessAsync(
        IServiceProvider services, MailMergeDbContext db, SendingAccount account, Campaign campaign, Message message,
        CancellationToken ct)
    {
        var contact = message.ContactId is { } contactId
            ? await db.Contacts.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(c => c.Id == contactId && c.WorkspaceId == campaign.WorkspaceId, ct)
            : null;
        if (contact is null)
        {
            await SetAsync(db, message.Id, MessageStatus.Skipped, "Contact was removed from the list", ct: ct);
            return false;
        }

        var optedOut = await services.GetRequiredService<OptOutRegistry>().IsOptedOutAsync(campaign.WorkspaceId, contact.Email, ct);
        if (DeliveryPlanner.SkipReason(contact, optedOut) is { } reason)
        {
            await SetAsync(db, message.Id, MessageStatus.Skipped, reason, ct: ct);
            return false;
        }

        var status = message.Status;
        var draftId = message.ProviderId;
        try
        {
            var outgoing = MessageBuilder.Build(campaign, contact.MergeData(), message.Email,
                await AttachmentsAsync(db, campaign, ct),
                campaign.UnsubscribeFooter ? unsubscribeLinks.Create(campaign.WorkspaceId, campaign.Id, message.Email) : null);
            var sender = senders.Create(account);

            if (sender.SupportsDrafts)
            {
                if (draftId is null)
                {
                    if (!await ClaimAsync(db, message.Id, status, MessageStatus.Drafting, ct))
                    {
                        return false;
                    }

                    status = MessageStatus.Drafting;
                    draftId = await sender.CreateDraftAsync(outgoing, ct);
                    await SetAsync(db, message.Id, status = MessageStatus.Drafted, null, draftId, ct);
                }
                else if (status == MessageStatus.Pending)
                {
                    // A retry of a message whose draft already exists: reuse it.
                    await SetAsync(db, message.Id, status = MessageStatus.Drafted, null, ct: ct);
                }

                if (campaign.Mode == CampaignMode.Send)
                {
                    if (!await ClaimAsync(db, message.Id, status, MessageStatus.Sending, ct))
                    {
                        return false;
                    }

                    status = MessageStatus.Sending;
                    await sender.SendDraftAsync(draftId, ct);
                    await SetAsync(db, message.Id, status = MessageStatus.Sent, null, ct: ct);
                }
            }
            else
            {
                if (campaign.Mode != CampaignMode.Send)
                {
                    throw new AccountProblemException("This account can't save drafts. Use Send instead.");
                }

                if (!await ClaimAsync(db, message.Id, status, MessageStatus.Sending, ct))
                {
                    return false;
                }

                status = MessageStatus.Sending;
                await sender.SendNowAsync(outgoing, ct);
                await SetAsync(db, message.Id, status = MessageStatus.Sent, null, ct: ct);
            }

            _streaks[campaign.Id] = 0;
            AppTelemetry.MessagesProcessed.Add(1, new KeyValuePair<string, object?>("outcome", status.ToString()));
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Shutting down: RecoverAsync resolves whatever state this message was left in.
            throw;
        }
        catch (AccountProblemException e)
        {
            // Nothing went out. Put the row back so Resume carries on from here.
            await SetAsync(db, message.Id, draftId is not null
                ? MessageStatus.Drafted
                : MessageStatus.Pending, null, ct: CancellationToken.None);
            await FinishAsync(db, campaign.Id, CampaignStatus.Paused, e.Message, CancellationToken.None);
            return false;
        }
        catch (Exception e)
        {
            var uncertain = e is DeliveryUncertainException
                            || (status == MessageStatus.Sending && e is not MessageRejectedException);
            var error = e is DeliveryUncertainException
                ? e.Message
                : uncertain
                    ? $"Unsure if this was sent ({e.Message}). Check Sent Items."
                    : e.Message;
            await SetAsync(db, message.Id, uncertain ? MessageStatus.Uncertain : MessageStatus.Failed, Trim(error),
                ct: CancellationToken.None);
            AppTelemetry.MessagesProcessed.Add(1, new KeyValuePair<string, object?>("outcome", uncertain ? "Uncertain" : "Failed"));
            log.LogWarning(e, "Message {MessageId} in campaign {CampaignId} {Outcome}", message.Id, campaign.Id,
                uncertain ? "is uncertain" : "failed");

            var streak = _streaks[campaign.Id] = _streaks.GetValueOrDefault(campaign.Id) + 1;
            if (streak >= MaxFailureStreak)
            {
                await FinishAsync(db, campaign.Id, CampaignStatus.Paused,
                    Trim($"Paused after {MaxFailureStreak} failures in a row. Last error: {e.Message}"), CancellationToken.None);
            }

            return true;
        }
    }

    private async Task<IReadOnlyList<OutgoingAttachment>> AttachmentsAsync(
        MailMergeDbContext db, Campaign campaign, CancellationToken ct)
    {
        // Attachments can't change while a campaign runs, so load the bytes once per run.
        if (!_attachments.TryGetValue(campaign.Id, out var attachments))
        {
            _attachments[campaign.Id] = attachments = await CampaignAttachments.LoadAsync(db, campaign, ct);
        }

        return attachments;
    }

    /// <summary>Moves a message from <paramref name="from"/> to <paramref name="to"/> only if nobody else has.</summary>
    private async Task<bool> ClaimAsync(MailMergeDbContext db, long messageId, MessageStatus from, MessageStatus to, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return await db.Messages.IgnoreQueryFilters().Where(m => m.Id == messageId && m.Status == from)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, to).SetProperty(m => m.Updated, now), ct) == 1;
    }

    private async Task SetAsync(
        MailMergeDbContext db, long messageId, MessageStatus status, string? error, string? providerId = null,
        CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var query = db.Messages.IgnoreQueryFilters().Where(m => m.Id == messageId);
        if (providerId is null)
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, status)
                .SetProperty(m => m.Error, error)
                .SetProperty(m => m.Updated, now), ct);
        }
        else
        {
            await query.ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, status)
                .SetProperty(m => m.Error, error)
                .SetProperty(m => m.ProviderId, providerId)
                .SetProperty(m => m.Updated, now), ct);
        }
    }

    private async Task FinishAsync(MailMergeDbContext db, long campaignId, CampaignStatus status, string? note, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Campaigns.IgnoreQueryFilters().Where(c => c.Id == campaignId && c.Status == CampaignStatus.Running)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, status)
                .SetProperty(c => c.Note, note)
                .SetProperty(c => c.LastActivityAt, now), ct);
        _streaks.Remove(campaignId);
        _attachments.Remove(campaignId);
    }

    private static string Trim(string s) => s.Length > 1000 ? s[..1000] : s;
}
