using SharedKernel.Results;

namespace MailMerge.Api.Features.OptOuts;

public static class OptOutErrors
{
    public static readonly Error NotFound = Error.NotFound("OptOut.NotFound", "That address isn't on the opt-out list");
}
