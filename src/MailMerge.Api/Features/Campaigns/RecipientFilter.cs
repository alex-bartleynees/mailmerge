using MailMerge.Api.Shared.Merging;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns;

public enum FilterMatch
{
    /// <summary>Every condition must hold (AND).</summary>
    All,

    /// <summary>At least one condition must hold (OR).</summary>
    Any,
}

public enum FilterOperator
{
    /// <summary>The cell says yes: TRUE, yes, y, 1, x, ✓ (any case).</summary>
    IsYes,

    /// <summary>The cell doesn't say yes, including blank.</summary>
    IsNo,

    Equals,
    NotEquals,
    Contains,
    IsEmpty,
    IsNotEmpty,
}

public sealed record FilterCondition(string Column, FilterOperator Operator, string? Value = null);

/// <summary>
/// "Only send to people where …": conditions on the contact's columns, e.g. <c>RSVP is yes</c> and
/// <c>SEND_EMAIL is yes</c>. Contacts that don't match are skipped, like opted-out contacts.
/// </summary>
public sealed record RecipientFilter(FilterMatch Match, IReadOnlyList<FilterCondition> Conditions)
{
    public const int MaxConditions = 10;

    private static readonly HashSet<string> YesValues =
        new(StringComparer.OrdinalIgnoreCase) { "true", "yes", "y", "1", "x", "✓", "✔", "t" };

    public bool IsEmpty => Conditions.Count == 0;

    public bool Matches(IReadOnlyDictionary<string, string> data) =>
        IsEmpty || (Match == FilterMatch.All
            ? Conditions.All(c => Holds(c, data))
            : Conditions.Any(c => Holds(c, data)));

    public static bool IsYes(string? value) => value is not null && YesValues.Contains(value.Trim());

    public static bool NeedsValue(FilterOperator op) =>
        op is FilterOperator.Equals or FilterOperator.NotEquals or FilterOperator.Contains;

    public Error? ValidateColumns(IReadOnlyList<string> columns)
    {
        var missing = Conditions.Select(c => c.Column.Trim())
            .Where(c => !columns.Contains(c, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return missing.Count == 0 ? null : Error.Validation("Campaign.FilterColumns",
            $"These recipient rule columns aren't in the selected list: {string.Join(", ", missing)}. Choose another column or remove the rule.");
    }

    private static bool Holds(FilterCondition condition, IReadOnlyDictionary<string, string> data)
    {
        var cell = MergeTemplate.Lookup(data, condition.Column).Trim();
        var value = (condition.Value ?? "").Trim();
        return condition.Operator switch
        {
            FilterOperator.IsYes => IsYes(cell),
            FilterOperator.IsNo => !IsYes(cell),
            FilterOperator.Equals => string.Equals(cell, value, StringComparison.OrdinalIgnoreCase),
            FilterOperator.NotEquals => !string.Equals(cell, value, StringComparison.OrdinalIgnoreCase),
            FilterOperator.Contains => cell.Contains(value, StringComparison.OrdinalIgnoreCase),
            FilterOperator.IsEmpty => cell.Length == 0,
            FilterOperator.IsNotEmpty => cell.Length > 0,
            _ => false,
        };
    }
}
