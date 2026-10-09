using System.Text.RegularExpressions;
using ClosedXML.Excel;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Lists.Export;

public sealed record ExportedFile(byte[] Content, string FileName);

public sealed partial class ExportListHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<ExportedFile>> Handle(long listId, CancellationToken ct)
    {
        var list = await db.ContactLists.AsNoTracking().SingleOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return ListErrors.NotFound;
        }

        var contacts = await db.Contacts.AsNoTracking().Where(c => c.ListId == listId).OrderBy(c => c.Id).ToListAsync(ct);
        var name = UnsafeFileChars().Replace(list.Name, "").Trim();
        return new ExportedFile(Write(list.Columns, contacts.Select(c => c.MergeData()).ToList()),
            (name.Length == 0 ? "contacts" : name) + ".xlsx");
    }

    private static byte[] Write(IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyDictionary<string, string>> records)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Contacts");
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c];
            for (var r = 0; r < records.Count; r++)
            {
                // Written as text so phone numbers keep their leading zero.
                sheet.Cell(r + 2, c + 1).SetValue(records[r].GetValueOrDefault(columns[c], ""));
            }

            var width = records.Take(200).Select(rec => rec.GetValueOrDefault(columns[c], "").Length)
                .Append(columns[c].Length).Max() + 2;
            sheet.Column(c + 1).Width = Math.Min(width, 50);
        }

        sheet.Row(1).Style.Font.Bold = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [GeneratedRegex(@"[^\w\- ]")]
    private static partial Regex UnsafeFileChars();
}
