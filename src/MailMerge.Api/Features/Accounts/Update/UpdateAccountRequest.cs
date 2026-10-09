using FluentValidation;

namespace MailMerge.Api.Features.Accounts.Update;

/// <summary>A blank or missing password means "keep the saved one". The account type can't change.</summary>
public sealed record UpdateAccountRequest(
    string? Name,
    string? FromEmail,
    string? FromName,
    int? PerMinute,
    SmtpSettings? Smtp,
    string? Password,
    string? ImapPassword);

internal sealed class UpdateAccountRequestValidator : AbstractValidator<UpdateAccountRequest>
{
    public UpdateAccountRequestValidator()
    {
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.FromName).MaximumLength(200);
        AccountRules.FromAndPacing(this, r => r.FromEmail, r => r.PerMinute);
        RuleFor(r => r.Smtp).ValidSmtp();
    }
}
