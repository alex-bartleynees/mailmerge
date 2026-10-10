using System.Text.RegularExpressions;
using Ganss.Xss;

namespace MailMerge.Api.Shared.Merging;

/// <summary>
/// Allowlist sanitiser for staff-written formatted HTML (email bodies and business footers). Saved HTML is put back
/// into the editor on the app's own origin, so anything that could run script there (event handlers,
/// <c>javascript:</c> links, iframes, forms) must never be stored. Keeps what the editor and normal pasted
/// formatting produce, and keeps <c>{{Field}}</c> merge tags intact even inside link addresses.
/// </summary>
public static partial class SafeHtml
{
    public static readonly string[] AllowedSchemes = ["http", "https", "mailto", "tel"];

    private static readonly HtmlSanitizer Sanitizer = Create();

    public static string Clean(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        // A sanitiser would percent-encode "{{Website}}" in an href and break the merge field. Swap tags for inert
        // tokens, sanitise, then restore them. The sanitiser always writes attributes in double quotes, so encoding
        // just " < > stops a crafted tag like {{a" onerror="x}} breaking out, without double-encoding an
        // apostrophe in a fallback such as {{Name|O'Brien}} when the email is merged.
        var tags = new List<string>();
        var tokenised = MergeTag().Replace(html, m =>
        {
            tags.Add(m.Value);
            return $"mmfield{tags.Count - 1}x";
        });

        var clean = Sanitizer.Sanitize(tokenised);
        return tags.Count == 0
            ? clean
            : Token().Replace(clean, m => EncodeBreakouts(tags[int.Parse(m.Groups[1].Value)]));
    }

    private static string EncodeBreakouts(string tag) =>
        tag.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static HtmlSanitizer Create()
    {
        var options = new HtmlSanitizerOptions
        {
            AllowedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "p", "div", "span", "br", "hr", "b", "strong", "i", "em", "u", "s", "small", "sub", "sup",
                "ul", "ol", "li", "a", "img", "h1", "h2", "h3", "h4", "blockquote",
                "table", "thead", "tbody", "tr", "th", "td",
            },
            AllowedAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "href", "src", "alt", "title", "width", "height", "style", "align", "colspan", "rowspan", "target",
            },
            AllowedSchemes = new HashSet<string>(AllowedSchemes, StringComparer.OrdinalIgnoreCase),
            UriAttributes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "href", "src" },
            AllowedCssProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "color", "background-color", "font-weight", "font-style", "font-size", "font-family",
                "text-decoration", "text-align", "margin", "margin-top", "margin-bottom", "padding",
                "line-height", "max-width", "max-height", "width", "height", "border", "border-collapse",
            },
        };
        var sanitizer = new HtmlSanitizer(options);
        // Links open safely in a new tab when a client honours target.
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { TagName: "A" } link && link.HasAttribute("target"))
            {
                link.SetAttribute("rel", "noopener noreferrer");
            }
        };
        sanitizer.AllowedAttributes.Add("rel");
        return sanitizer;
    }

    [GeneratedRegex(@"\{\{[^}]*\}\}")]
    private static partial Regex MergeTag();

    [GeneratedRegex(@"mmfield(\d+)x")]
    private static partial Regex Token();
}
