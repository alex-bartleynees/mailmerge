using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.ExportLog;

public sealed record CampaignLog(byte[] Content, string FileName);

/// <summary>Who was emailed and what happened, as a CSV that opens in Excel.</summary>
public sealed partial class ExportLogHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<CampaignLog>> Handle(long campaignId, CancellationToken ct)
    {
        var name = await db.Campaigns.Where(c => c.Id == campaignId).Select(c => c.Name).SingleOrDefaultAsync(ct);
        if (name is null)
        {
            return CampaignErrors.NotFound;
        }

        var rows = await db.Messages.AsNoTracking().Where(m => m.CampaignId == campaignId).OrderBy(m => m.Id)
            .Select(m => new { m.Email, m.Status, m.Error, m.Updated })
            .ToListAsync(ct);

        var csv = new StringBuilder("Email,Status,Note,Updated (UTC)\r\n");
        foreach (var r in rows)
        {
            csv.Append(Field(r.Email)).Append(',')
                .Append(CampaignDetails.StatusKey(r.Status)).Append(',')
                .Append(Field(r.Error ?? "")).Append(',')
                .Append(r.Updated.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        var safe = UnsafeFileChars().Replace(name, "").Trim();
        // BOM so Excel opens it as UTF-8.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return new CampaignLog(bytes, $"{(safe.Length == 0 ? "email" : safe)} log.csv");
    }

    /// <summary>Quotes when needed, and defuses values Excel would run as formulas.</summary>
    private static string Field(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    [GeneratedRegex(@"[^\w\- ]")]
    private static partial Regex UnsafeFileChars();
}
