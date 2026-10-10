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
    public static string Render(string? template, IReadOnlyDictionary<string, string> data, bool asHtml)
    {
        var source = template ?? "";
        return Tag().Replace(source, match =>
        {
            var value = Lookup(data, match.Groups[1].Value);
            if (string.IsNullOrWhiteSpace(value))
            {
                value = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
            }

            if (!asHtml)
            {
                return value;
            }

            // A field that starts a link or image address decides its scheme, so only safe URLs may go there.
            if (StartsUrlAttribute(source, match.Index))
            {
                value = SafeUrl(value);
            }

            return WebUtility.HtmlEncode(value);
        });
    }

    /// <summary>
    /// <paramref name="value"/> if it's an http(s)/mailto/tel URL; <c>https://</c> + value if it looks like a bare
    /// domain (shop.co.nz/page); otherwise empty, so <c>javascript:</c> and other schemes never reach a link.
    /// </summary>
    public static string SafeUrl(string value)
    {
        var v = value.Trim();
        if (v.Length == 0)
        {
            return "";
        }

        if (Uri.TryCreate(v, UriKind.Absolute, out var uri) && !v.StartsWith('/'))
        {
            return SafeHtml.AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase) ? v : "";
        }

        return BareDomain().IsMatch(v) ? "https://" + v : "";
    }

    /// <summary>True when the tag at <paramref name="index"/> is the first thing in an href/src attribute value.</summary>
    private static bool StartsUrlAttribute(string template, int index)
    {
        var open = template.LastIndexOf('<', index);
        if (open < 0 || template.LastIndexOf('>', index) > open)
        {
            return false;
        }

        return UrlAttributeStart().IsMatch(template[open..index]);
    }

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

    /// <summary>The value a field would merge to before any fallback: exact name first, then case-insensitive.</summary>
    public static string Lookup(IReadOnlyDictionary<string, string> data, string key)
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

    [GeneratedRegex(@"\b(?:href|src)\s*=\s*[""']?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttributeStart();

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)*\.[a-z]{2,}(:\d+)?([/?#]\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex BareDomain();

    [GeneratedRegex(@"<br\s*/?>|</p>|</div>|</li>|</h[1-6]>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnds();

    [GeneratedRegex("<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex Tags();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}
