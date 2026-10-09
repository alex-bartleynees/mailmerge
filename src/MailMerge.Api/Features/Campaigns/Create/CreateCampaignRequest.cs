using FluentValidation;

namespace MailMerge.Api.Features.Campaigns.Create;

public sealed record CreateCampaignRequest(string? Name);

internal sealed class CreateCampaignRequestValidator : AbstractValidator<CreateCampaignRequest>
{
    public CreateCampaignRequestValidator() => RuleFor(r => r.Name).MaximumLength(200);
}
