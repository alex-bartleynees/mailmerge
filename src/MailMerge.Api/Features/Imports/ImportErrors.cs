using SharedKernel.Results;

namespace MailMerge.Api.Features.Imports;

public static class ImportErrors
{
    public static readonly Error NoFile = Error.Validation("Import.NoFile", "No file received");

    public static readonly Error UnsupportedFile = Error.Validation("Import.UnsupportedFile",
        "Please upload an Excel .xlsx file (or a .csv). Older .xls files: open in Excel and Save As .xlsx.");

    public static Error TooLarge(int megabytes) =>
        Error.Validation("Import.TooLarge", $"That file is too large (the limit is {megabytes} MB).");

    public static readonly Error Expired =
        Error.NotFound("Import.Expired", "That upload has expired. Please choose the file again.");

    public static readonly Error EmailColumnRequired =
        Error.Validation("Import.EmailColumnRequired", "Choose which column holds the email addresses");

    public static Error Unreadable(string message) => Error.Validation("Import.Unreadable", message);
}
