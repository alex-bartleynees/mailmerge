using FluentValidation;

namespace MailMerge.Api.Features.OptOuts.Add;

/// <summary><c>Emails</c> is free text: addresses separated by spaces, commas, semicolons or new lines.</summary>
public sealed record AddOptOutsRequest(string Emails, string? Reason);

internal sealed class AddOptOutsRequestValidator : AbstractValidator<AddOptOutsRequest>
{
    public AddOptOutsRequestValidator()
    {
        RuleFor(r => r.Emails).NotEmpty().WithMessage("Enter at least one email address");
        RuleFor(r => r.Reason).MaximumLength(500);
    }
}
