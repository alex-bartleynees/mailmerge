using MailMerge.Api.Shared.Merging;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Imports;

/// <summary>What staff see before importing: sheets, columns, a few rows, and how many addresses look wrong.</summary>
public sealed record ImportPreview(
    Guid Token,
    string FileName,
    IReadOnlyList<string> Sheets,
    string Sheet,
    IReadOnlyList<string> Headers,
    string EmailColumn,
    int RowCount,
    IReadOnlyList<IReadOnlyList<string>> Sample,
    int InvalidCount,
    int BlankCount)
{
    public const int SampleRows = 6;

    public static Result<ImportPreview> Build(ImportUpload upload, string? sheet)
    {
        try
        {
            var sheets = SpreadsheetReader.SheetNames(upload.Content, upload.FileName);
            var chosen = sheet is not null && sheets.Contains(sheet) ? sheet : sheets[0];
            var table = SpreadsheetReader.Read(upload.Content, upload.FileName, chosen);
            var emailColumn = EmailColumnDetector.Detect(table);
            var emails = emailColumn.Length == 0
                ? []
                : table.Rows.Select(r => r[table.Headers.ToList().IndexOf(emailColumn)]).ToList();

            return new ImportPreview(upload.Token, upload.FileName, sheets, chosen, table.Headers, emailColumn,
                table.Rows.Count, table.Rows.Take(SampleRows).ToList(),
                emails.Count(e => e.Length > 0 && !EmailAddress.IsValid(e)),
                emails.Count(e => e.Length == 0));
        }
        catch (SpreadsheetException e)
        {
            return ImportErrors.Unreadable(e.Message);
        }
    }
}
