using FluentValidation;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Privacy.ErasePerson;

/// <summary>
/// <c>KeepOptedOut</c> (default true) leaves a hash-only opt-out so an old spreadsheet can't re-add them. Untick only
/// when the person wants to hear from the shop again later.
/// </summary>
public sealed record ErasePersonRequest(string Email, bool KeepOptedOut = true);

internal sealed class ErasePersonRequestValidator : AbstractValidator<ErasePersonRequest>
{
    public ErasePersonRequestValidator() =>
        RuleFor(r => r.Email).Must(EmailAddress.IsValid).WithMessage("Enter the person's email address");
}
