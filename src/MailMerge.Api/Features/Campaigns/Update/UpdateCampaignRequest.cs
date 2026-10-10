using FluentValidation;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Campaigns.Update;

/// <summary>
/// Only fields that are present change. <c>Filter</c> with no conditions removes the filter (everyone on the list).
/// </summary>
public sealed record UpdateCampaignRequest(
    string? Name,
    long? AccountId,
    long? ListId,
    string? Subject,
    string? Body,
    bool? IsHtml,
    bool? UnsubscribeFooter,
    RecipientFilter? Filter,
    string? UnsubscribeMessage = null,
    bool? IncludeBusinessFooter = null,
    bool? UseWorkspaceBusinessFooter = null,
    BusinessFooter? BusinessFooter = null);

internal sealed class UpdateCampaignRequestValidator : AbstractValidator<UpdateCampaignRequest>
{
    public UpdateCampaignRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.Subject).MaximumLength(998).WithMessage("The subject is too long");
        RuleFor(r => r.Body).MaximumLength(2_000_000).WithMessage("The message is too long");
        RuleFor(r => r.UnsubscribeMessage).MaximumLength(1000).WithMessage("The unsubscribe message is too long");
        RuleFor(r => r.BusinessFooter!).SetValidator(new BusinessFooterValidator()).When(r => r.BusinessFooter is not null);
        RuleFor(r => r.Filter!.Match).IsInEnum().When(r => r.Filter is not null);
        RuleFor(r => r.Filter!.Conditions)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(c => c.Count <= RecipientFilter.MaxConditions)
            .WithMessage($"A filter can have at most {RecipientFilter.MaxConditions} conditions")
            .When(r => r.Filter is not null);
        RuleForEach(r => r.Filter!.Conditions).NotNull().WithMessage("Each filter condition must be provided").ChildRules(c =>
        {
            c.RuleFor(x => x.Column).NotEmpty().WithMessage("Choose a column for each filter condition");
            c.RuleFor(x => x.Operator).IsInEnum().WithMessage("Choose how each filter condition compares");
            c.RuleFor(x => x.Value).NotEmpty().When(x => RecipientFilter.NeedsValue(x.Operator))
                .WithMessage("Enter the value to compare with");
        }).When(r => r.Filter?.Conditions is not null);
    }
}
