using FluentValidation;

namespace MailMerge.Api.Features.Lists;

public static class ColumnName
{
    /// <summary>Merge-field syntax characters can't appear in a column name, or <c>{{Name}}</c> couldn't refer to it.</summary>
    public static IRuleBuilderOptions<T, string> ValidColumnName<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(n => !string.IsNullOrWhiteSpace(n) && n.Trim().Length <= 100 && n.IndexOfAny(['{', '}', '|']) < 0)
            .WithMessage("Column names can't be empty or contain { } |");
}
