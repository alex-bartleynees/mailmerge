using FluentValidation;

namespace MailMerge.Api.Features.Campaigns.MarkMessage;

/// <summary>
/// <c>Sent</c>: "I checked Sent Items, it went". <c>Pending</c>: "retry it". <c>Skipped</c>: "don't send it".
/// </summary>
public sealed record MarkMessageRequest(MessageStatus Status);

internal sealed class MarkMessageRequestValidator : AbstractValidator<MarkMessageRequest>
{
    public MarkMessageRequestValidator() =>
        RuleFor(r => r.Status).Must(s => s is MessageStatus.Sent or MessageStatus.Pending or MessageStatus.Skipped)
            .WithMessage("A message can only be marked as sent, retry, or skip");
}
