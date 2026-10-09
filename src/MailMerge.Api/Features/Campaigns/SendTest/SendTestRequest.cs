using FluentValidation;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Campaigns.SendTest;

public sealed record SendTestRequest(string To);

internal sealed class SendTestRequestValidator : AbstractValidator<SendTestRequest>
{
    public SendTestRequestValidator() =>
        RuleFor(r => r.To).Must(EmailAddress.IsValid).WithMessage("Enter your own email address to send the test to");
}
