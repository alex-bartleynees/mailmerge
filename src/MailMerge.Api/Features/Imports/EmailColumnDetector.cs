using System.Text.RegularExpressions;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Imports;

public static partial class EmailColumnDetector
{
    private static readonly string[] ExactNames = ["email", "emailaddress", "emailaddr", "mail"];

    /// <summary>
    /// Best guess at which column holds email addresses: an exact header name, then the column where at least
    /// half of the first 50 rows look like addresses, then a header mentioning email/mail. Empty if none.
    /// </summary>
    public static string Detect(SheetTable table)
    {
        var normalized = table.Headers.Select(h => Separators().Replace(h.ToLowerInvariant(), "")).ToList();

        var exact = normalized.FindIndex(n => ExactNames.Contains(n));
        if (exact >= 0)
        {
            return table.Headers[exact];
        }

        var partial = normalized.FindIndex(n => n.Contains("email") || n.Contains("mail"));
        var sample = table.Rows.Take(50).ToList();
        var best = "";
        var bestScore = 0.0;
        for (var i = 0; i < table.Headers.Count && sample.Count > 0; i++)
        {
            var score = sample.Count(r => EmailAddress.IsValid(r[i])) / (double)sample.Count;
            if (score > bestScore && score >= 0.5)
            {
                best = table.Headers[i];
                bestScore = score;
            }
        }

        // A flag such as SEND_EMAIL should not beat an address column named "Contact".
        // Keep the header-only fallback for empty lists and unfamiliar address formats.
        return best.Length > 0 ? best : partial >= 0 ? table.Headers[partial] : "";
    }

    [GeneratedRegex(@"[\s_\-]")]
    private static partial Regex Separators();
}
