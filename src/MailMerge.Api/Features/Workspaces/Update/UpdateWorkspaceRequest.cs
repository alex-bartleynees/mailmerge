using FluentValidation;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Workspaces.Update;

/// <summary>Shop name (shown on unsubscribe pages) and how long per-recipient history is kept.</summary>
public sealed record UpdateWorkspaceRequest(string? Name, int? RetentionDays, BusinessFooter? BusinessFooter = null);

internal sealed class UpdateWorkspaceRequestValidator : AbstractValidator<UpdateWorkspaceRequest>
{
    public const int MinRetentionDays = 7;
    public const int MaxRetentionDays = 3650;

    public UpdateWorkspaceRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.BusinessFooter!).SetValidator(new BusinessFooterValidator()).When(r => r.BusinessFooter is not null);
        RuleFor(r => r.RetentionDays).InclusiveBetween(MinRetentionDays, MaxRetentionDays)
            .WithMessage($"Keep history for between {MinRetentionDays} and {MaxRetentionDays} days");
    }
}
