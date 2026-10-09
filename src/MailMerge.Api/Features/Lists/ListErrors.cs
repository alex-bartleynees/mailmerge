using SharedKernel.Results;

namespace MailMerge.Api.Features.Lists;

public static class ListErrors
{
    public static readonly Error NotFound = Error.NotFound("List.NotFound", "List not found");

    public static readonly Error ColumnExists = Error.Conflict("List.ColumnExists", "That column already exists");

    public static readonly Error EmailColumnRequired =
        Error.Validation("List.EmailColumnRequired", "The Email column can't be removed");
}
