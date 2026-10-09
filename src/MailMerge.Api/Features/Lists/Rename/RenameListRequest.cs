using FluentValidation;

namespace MailMerge.Api.Features.Lists.Rename;

public sealed record RenameListRequest(string Name);

internal sealed class RenameListRequestValidator : AbstractValidator<RenameListRequest>
{
    public RenameListRequestValidator() =>
        RuleFor(r => r.Name).NotEmpty().WithMessage("Give the list a name").MaximumLength(200);
}
