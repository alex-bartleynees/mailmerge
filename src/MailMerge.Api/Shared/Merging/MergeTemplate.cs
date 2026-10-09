using System.Net;
using System.Text.RegularExpressions;

namespace MailMerge.Api.Shared.Merging;

/// <summary><c>{{Column}}</c> and <c>{{Column|fallback}}</c> merge fields.</summary>
public static partial class MergeTemplate
{
    [GeneratedRegex(@"\{\{\s*([^}|]+?)\s*(?:\|([^}]*))?\}\}")]
    private static partial Regex Tag();

    /// <summary>
    /// Replaces each field with the contact's value (column names match case-insensitively). Blank values use the
    /// fallback. In HTML mode values are HTML-escaped so a contact's data can't inject markup.
    /// </summary>
    public static string Render(string? template, IReadOnlyDictionary<string, string> data, bool asHtml) =>
        Tag().Replace(template ?? "", match =>
        {
            var value = Lookup(data, match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
            }

            return asHtml ? WebUtility.HtmlEncode(value) : value;
        });

    /// <summary>Field names used in the template, in order of appearance (duplicates kept).</summary>
    public static IReadOnlyList<string> UsedFields(string? template) =>
        Tag().Matches(template ?? "").Select(m => m.Groups[1].Value.Trim()).ToList();

    public static string HtmlToText(string html)
    {
        var s = BlockEnds().Replace(html, "\n");
        s = Tags().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        return BlankLines().Replace(s, "\n\n").Trim();
    }

    private static string Lookup(IReadOnlyDictionary<string, string> data, string key)
    {
        if (data.TryGetValue(key, out var exact))
        {
            return exact ?? "";
        }

        var wanted = key.Trim();
        foreach (var (name, value) in data)
        {
            if (string.Equals(name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return value ?? "";
            }
        }

        return "";
    }

    [GeneratedRegex(@"<br\s*/?>|</p>|</div>|</li>|</h[1-6]>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnds();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
