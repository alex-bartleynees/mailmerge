using FluentValidation;

namespace MailMerge.Api.Features.Campaigns.Run;

/// <summary><c>Draft</c>: create drafts for review. <c>Send</c>: send everyone (existing drafts are sent as they are).</summary>
public sealed record RunCampaignRequest(CampaignMode Mode);

internal sealed class RunCampaignRequestValidator : AbstractValidator<RunCampaignRequest>
{
    public RunCampaignRequestValidator() => RuleFor(r => r.Mode).IsInEnum().WithMessage("Mode must be draft or send");
}
