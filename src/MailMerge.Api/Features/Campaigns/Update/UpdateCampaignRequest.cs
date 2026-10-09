using FluentValidation;

namespace MailMerge.Api.Features.Campaigns.Update;

/// <summary>Only fields that are present change.</summary>
public sealed record UpdateCampaignRequest(
    string? Name,
    long? AccountId,
    long? ListId,
    string? Subject,
    string? Body,
    bool? IsHtml,
    bool? UnsubscribeFooter);

internal sealed class UpdateCampaignRequestValidator : AbstractValidator<UpdateCampaignRequest>
{
    public UpdateCampaignRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.Subject).MaximumLength(998).WithMessage("The subject is too long");
        RuleFor(r => r.Body).MaximumLength(2_000_000).WithMessage("The message is too long");
    }
}
