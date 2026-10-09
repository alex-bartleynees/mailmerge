using FluentValidation;

namespace MailMerge.Api.Features.Lists.Create;

public sealed record CreateListRequest(string? Name);

internal sealed class CreateListRequestValidator : AbstractValidator<CreateListRequest>
{
    public CreateListRequestValidator() => RuleFor(r => r.Name).MaximumLength(200);
}
