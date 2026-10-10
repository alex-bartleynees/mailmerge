using System.Text.Json;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.Campaigns;

public sealed record AttachmentView(long Id, string FileName, string ContentType, long Size, bool Available);

/// <summary>An email campaign with its progress counts (keyed by message status, plus "total").
/// <c>PurgesOn</c> is when retention will archive it (null while running, archived, or awaiting an "uncertain" decision).</summary>
public sealed record CampaignDetails(
    long Id,
    string Name,
    long? AccountId,
    long? ListId,
    string Subject,
    string Body,
    bool IsHtml,
    CampaignMode Mode,
    CampaignStatus Status,
    string? Note,
    bool UnsubscribeFooter,
    RecipientFilter? Filter,
    DateTimeOffset Created,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? ArchivedAt,
    DateTimeOffset? PurgesOn,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<AttachmentView> Attachments,
    string? UnsubscribeMessage = null,
    bool IncludeBusinessFooter = false,
    bool UseWorkspaceBusinessFooter = true,
    BusinessFooter? BusinessFooter = null)
{
    public static async Task<IReadOnlyList<CampaignDetails>> LoadAsync(
        MailMergeDbContext db, IQueryable<Campaign> campaigns, CancellationToken ct)
    {
        var rows = await campaigns.AsNoTracking().ToListAsync(ct);
        var ids = rows.Select(c => c.Id).ToList();
        var retentionDays = rows.Count == 0
            ? 0
            : await db.Workspaces.Where(w => w.Id == rows[0].WorkspaceId).Select(w => w.RetentionDays).SingleAsync(ct);
        var blocked = await db.Messages.AsNoTracking()
            .Where(m => ids.Contains(m.CampaignId) && (m.Status == MessageStatus.Uncertain || m.Status == MessageStatus.Drafting || m.Status == MessageStatus.Sending))
            .Select(m => m.CampaignId).Distinct().ToListAsync(ct);

        var counts = await db.Messages.AsNoTracking()
            .Where(m => ids.Contains(m.CampaignId))
            .GroupBy(m => new { m.CampaignId, m.Status })
            .Select(g => new { g.Key.CampaignId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var attachments = await db.Attachments.AsNoTracking()
            .Where(a => ids.Contains(a.CampaignId))
            .OrderBy(a => a.Id)
            .Select(a => new { a.CampaignId, View = new AttachmentView(a.Id, a.FileName, a.ContentType, a.Size, a.Content != null) })
            .ToListAsync(ct);

        return rows.Select(c => new CampaignDetails(
                c.Id, c.Name, c.AccountId, c.ListId, c.Subject, c.Body, c.IsHtml, c.Mode, c.Status, c.Note,
                c.UnsubscribeFooter, c.Filter, c.Created, c.LastActivityAt, c.ArchivedAt,
                c.IsArchived || c.Status == CampaignStatus.Running || blocked.Contains(c.Id) ? null : c.LastActivityAt.AddDays(retentionDays),
                c.Summary ?? StatusCounts(counts.Where(x => x.CampaignId == c.Id).Select(x => (x.Status, x.Count))),
                attachments.Where(a => a.CampaignId == c.Id).Select(a => a.View).ToList(), c.UnsubscribeMessage,
                c.IncludeBusinessFooter, c.UseWorkspaceBusinessFooter, c.BusinessFooter))
            .ToList();
    }

    public static async Task<CampaignDetails?> LoadOneAsync(MailMergeDbContext db, long campaignId, CancellationToken ct) =>
        (await LoadAsync(db, db.Campaigns.Where(c => c.Id == campaignId), ct)).SingleOrDefault();

    /// <summary>{ "sent": 3, "failed": 1, "total": 4 }: the shape the UI's progress bar reads.</summary>
    public static Dictionary<string, int> StatusCounts(IEnumerable<(MessageStatus Status, int Count)> counts)
    {
        var result = counts.ToDictionary(x => StatusKey(x.Status), x => x.Count);
        result["total"] = result.Values.Sum();
        return result;
    }

    public static string StatusKey(MessageStatus status) => JsonNamingPolicy.CamelCase.ConvertName(status.ToString());
}
