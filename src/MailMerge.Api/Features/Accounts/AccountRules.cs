using FluentValidation;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Accounts;

internal static class AccountRules
{
    public const int MaxPerMinute = 120;

    public static void FromAndPacing<T>(AbstractValidator<T> v, Func<T, string?> fromEmail, Func<T, int?> perMinute)
    {
        v.RuleFor(r => fromEmail(r))
            .Must(e => string.IsNullOrWhiteSpace(e) || EmailAddress.IsValid(e))
            .WithMessage("The \"from\" address doesn't look like an email address");
        v.RuleFor(r => perMinute(r))
            .InclusiveBetween(1, MaxPerMinute)
            .When(r => perMinute(r) is not null)
            .WithMessage($"Emails per minute must be between 1 and {MaxPerMinute}");
    }

    public static IRuleBuilderOptions<T, SmtpSettings?> ValidSmtp<T>(this IRuleBuilder<T, SmtpSettings?> rule) =>
        rule.Must(s => s is null || (s.Port is > 0 and < 65536 && s.ImapPort is > 0 and < 65536
                                     && IsSecurity(s.Security) && IsSecurity(s.ImapSecurity)))
            .WithMessage("Check the mail server port and security settings");

    private static bool IsSecurity(string s) => s is "starttls" or "ssl" or "none";
}
