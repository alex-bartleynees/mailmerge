using System.Text.RegularExpressions;

namespace MailMerge.Api.Shared.Merging;

public static partial class EmailAddress
{
    /// <summary>Deliberately loose: catches typos and junk, not every RFC edge case.</summary>
    [GeneratedRegex("""^[^@\s<>",;]+@[^@\s<>",;]+\.[^@\s<>",;]{2,}$""")]
    private static partial Regex Pattern();

    public static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254)
        {
            return false;
        }

        return Pattern().IsMatch(value.Trim());
    }

    /// <summary>How addresses are stored and compared everywhere: trimmed, lower-case.</summary>
    public static string Normalize(string? value) => (value ?? "").Trim().ToLowerInvariant();
}
