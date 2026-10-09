using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Privacy.Retention;

/// <summary>
/// Deletes per-recipient rows and attachment bytes once a campaign has been idle for the workspace's retention period,
/// keeping the campaign itself (name, text, summary counts) as an archived record. A campaign that is running, or has
/// messages still awaiting a human decision (uncertain), is never purged. Also clears expired uploads and sign-ins.
/// </summary>
public sealed class RetentionService(IServiceScopeFactory scopes, TimeProvider clock, ILogger<RetentionService> log)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    public static readonly TimeSpan ImportLifetime = TimeSpan.FromHours(24);

    private static readonly MessageStatus[] Unresolved =
        [MessageStatus.Uncertain, MessageStatus.Drafting, MessageStatus.Sending];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), clock, stoppingToken);
            using var timer = new PeriodicTimer(Interval, clock);
            do
            {
                try
                {
                    await PurgeAsync(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "Retention purge failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    /// <summary>One purge pass over every workspace. Returns the number of campaigns archived.</summary>
    public async Task<int> PurgeAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MailMergeDbContext>();
        var now = clock.GetUtcNow();

        await db.ImportUploads.IgnoreQueryFilters().Where(u => u.Created < now - ImportLifetime).ExecuteDeleteAsync(ct);
        await db.PendingMicrosoftSignIns.Where(s => s.Expires < now).ExecuteDeleteAsync(ct);

        var archived = 0;
        var workspaces = await db.Workspaces.AsNoTracking().Select(w => new { w.Id, w.RetentionDays }).ToListAsync(ct);
        foreach (var workspace in workspaces)
        {
            var cutoff = now.AddDays(-workspace.RetentionDays);
            var campaigns = db.Campaigns.IgnoreQueryFilters();
            var messages = db.Messages.IgnoreQueryFilters();
            var eligible = await campaigns
                .Where(c => c.WorkspaceId == workspace.Id && c.ArchivedAt == null && c.Status != CampaignStatus.Running
                            && c.LastActivityAt < cutoff
                            && !messages.Any(m => m.CampaignId == c.Id && Unresolved.Contains(m.Status)))
                .Select(c => c.Id)
                .ToListAsync(ct);
            if (eligible.Count == 0)
            {
                continue;
            }

            int purgedMessages = 0, purgedAttachments = 0;
            foreach (var campaignId in eligible)
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var counts = await messages.Where(m => m.CampaignId == campaignId)
                    .GroupBy(m => m.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
                var summary = CampaignDetails.StatusCounts(counts.Select(c => (c.Key, c.Count)));

                purgedMessages += await messages.Where(m => m.CampaignId == campaignId).ExecuteDeleteAsync(ct);
                purgedAttachments += await db.Attachments.IgnoreQueryFilters()
                    .Where(a => a.CampaignId == campaignId && a.Content != null)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Content, (byte[]?)null), ct);
                // Re-checks the conditions so a campaign started meanwhile is left alone.
                var updated = await campaigns
                    .Where(c => c.Id == campaignId && c.Status != CampaignStatus.Running && c.ArchivedAt == null)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.ArchivedAt, now)
                        .SetProperty(c => c.Summary, summary)
                        .SetProperty(c => c.Status, CampaignStatus.Done)
                        .SetProperty(c => c.Note, (string?)null), ct);
                if (updated == 1)
                {
                    await tx.CommitAsync(ct);
                    archived++;
                }
            }

            db.PrivacyLog.Add(new PrivacyLogEntry
            {
                WorkspaceId = workspace.Id,
                Action = "retention_purge",
                PerformedBy = "MailMerge (automatic)",
                PerformedAt = now,
                Counts = new() { ["campaigns"] = eligible.Count, ["messages"] = purgedMessages, ["attachments"] = purgedAttachments },
            });
            await db.SaveChangesAsync(ct);
            log.LogInformation("Retention archived {Campaigns} campaign(s) in workspace {WorkspaceId}", eligible.Count, workspace.Id);
        }

        return archived;
    }
}
