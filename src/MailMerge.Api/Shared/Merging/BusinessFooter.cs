using System.Net;
using FluentValidation;

namespace MailMerge.Api.Shared.Merging;

/// <summary>Optional business details in formatted text, with an optional hosted logo.</summary>
public sealed record BusinessFooter(string Html, string? LogoUrl = null)
{
    /// <summary>The same footer with its HTML passed through <see cref="SafeHtml"/>. Apply before saving.</summary>
    public BusinessFooter Sanitised() => this with { Html = SafeHtml.Clean(Html) };

    public string Render(bool asHtml)
    {
        var text = MergeTemplate.HtmlToText(Html);
        if (asHtml)
        {
            var logo = string.IsNullOrWhiteSpace(LogoUrl) ? ""
                : $"""<p><img src="{WebUtility.HtmlEncode(LogoUrl)}" alt="Business logo" style="max-width:200px;max-height:100px"></p>""";
            return string.IsNullOrWhiteSpace(Html) && logo.Length == 0 ? ""
                : $"""<div style="margin-top:24px">{Html}{logo}</div>""";
        }

        return text.Length == 0 ? "" : $"\n\n--\n{text}\n";
    }
}

internal sealed class BusinessFooterValidator : AbstractValidator<BusinessFooter>
{
    public BusinessFooterValidator()
    {
        RuleFor(f => f.Html).NotNull().MaximumLength(20_000);
        RuleFor(f => f.LogoUrl).MaximumLength(2048)
            .Must(url => string.IsNullOrWhiteSpace(url)
                         || Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
            .WithMessage("Use an http or https address for the business logo");
    }
}
