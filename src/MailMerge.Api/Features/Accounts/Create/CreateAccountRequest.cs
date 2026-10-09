using FluentValidation;

namespace MailMerge.Api.Features.Accounts.Create;

public sealed record CreateAccountRequest(
    string? Name,
    AccountKind Kind,
    string? FromEmail,
    string? FromName,
    int? PerMinute,
    SmtpSettings? Smtp,
    string? Password,
    string? ImapPassword);

internal sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(r => r.Kind).IsInEnum().WithMessage("Pick an account type");
        RuleFor(r => r.Name).MaximumLength(200);
        RuleFor(r => r.FromName).MaximumLength(200);
        AccountRules.FromAndPacing(this, r => r.FromEmail, r => r.PerMinute);
        RuleFor(r => r.Smtp).ValidSmtp();
        RuleFor(r => r.Smtp!.Host).NotEmpty().When(r => r.Kind == AccountKind.Smtp)
            .WithMessage("Enter the mail server (SMTP) address");
    }
}
