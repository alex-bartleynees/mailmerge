using FluentValidation;

namespace MailMerge.Api.Features.Lists.AddColumn;

public sealed record AddColumnRequest(string Name);

internal sealed class AddColumnRequestValidator : AbstractValidator<AddColumnRequest>
{
    public AddColumnRequestValidator() => RuleFor(r => r.Name).ValidColumnName();
}
