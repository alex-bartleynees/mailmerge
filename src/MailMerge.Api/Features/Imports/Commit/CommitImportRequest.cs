using FluentValidation;

namespace MailMerge.Api.Features.Imports.Commit;

/// <summary>Import into <c>ListId</c>, or into a new list called <c>NewListName</c> when it's null.</summary>
public sealed record CommitImportRequest(
    string? Sheet,
    string EmailColumn,
    long? ListId,
    string? NewListName,
    bool UpdateExisting);

internal sealed class CommitImportRequestValidator : AbstractValidator<CommitImportRequest>
{
    public CommitImportRequestValidator()
    {
        RuleFor(r => r.EmailColumn).NotEmpty().WithMessage("Choose which column holds the email addresses");
        RuleFor(r => r.NewListName).MaximumLength(200);
    }
}
