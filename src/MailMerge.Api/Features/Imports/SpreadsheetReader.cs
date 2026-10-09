using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using nietras.SeparatedValues;

namespace MailMerge.Api.Features.Imports;

public sealed class SpreadsheetException(string message) : Exception(message);

/// <summary>A header row plus data rows, every row padded to the header width.</summary>
public sealed record SheetTable(IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    public static readonly SheetTable Empty = new([], []);
}

/// <summary>Reads Excel (.xlsx/.xlsm) and CSV/TSV files the way staff expect them to look in Excel.</summary>
public static class SpreadsheetReader
{
    public const string CsvSheetName = "CSV";

    private static readonly string[] CsvExtensions = [".csv", ".txt", ".tsv"];
    private static readonly string[] ExcelExtensions = [".xlsx", ".xlsm"];

    public static bool IsSupported(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return CsvExtensions.Contains(ext) || ExcelExtensions.Contains(ext);
    }

    public static IReadOnlyList<string> SheetNames(byte[] content, string fileName)
    {
        if (IsCsv(fileName))
        {
            return [CsvSheetName];
        }

        using var workbook = OpenWorkbook(content);
        return workbook.Worksheets.Select(w => w.Name).ToList();
    }

    public static SheetTable Read(byte[] content, string fileName, string? sheet = null)
    {
        var table = IsCsv(fileName) ? ReadCsv(content) : ReadExcel(content, sheet);
        return Normalize(table);
    }

    private static bool IsCsv(string fileName) =>
        CsvExtensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());

    private static XLWorkbook OpenWorkbook(byte[] content)
    {
        try
        {
            return new XLWorkbook(new MemoryStream(content));
        }
        catch (Exception)
        {
            throw new SpreadsheetException(
                "Couldn't open that file. Please use an Excel .xlsx file or a .csv " +
                "(in Excel: File > Save As > Excel Workbook).");
        }
    }

    private static List<List<string>> ReadExcel(byte[] content, string? sheet)
    {
        using var workbook = OpenWorkbook(content);
        var worksheet = sheet is not null && workbook.Worksheets.TryGetWorksheet(sheet, out var named)
            ? named
            : workbook.Worksheet(1);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastColumn = worksheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var table = new List<List<string>>(lastRow);
        for (var r = 1; r <= lastRow; r++)
        {
            var row = new List<string>(lastColumn);
            for (var c = 1; c <= lastColumn; c++)
            {
                row.Add(CellText(worksheet.Cell(r, c).Value));
            }

            table.Add(row);
        }

        return table;
    }

    private static string CellText(XLCellValue value)
    {
        if (value.IsBlank || value.IsError)
        {
            return "";
        }

        if (value.IsNumber)
        {
            var n = value.GetNumber();
            // Phone numbers and postcodes stored as numbers shouldn't come out as "21234567.0".
            return n == Math.Floor(n) && Math.Abs(n) < 1e15
                ? ((long)n).ToString(CultureInfo.InvariantCulture)
                : n.ToString(CultureInfo.InvariantCulture);
        }

        if (value.IsDateTime)
        {
            return value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (value.IsBoolean)
        {
            return value.GetBoolean() ? "TRUE" : "FALSE";
        }

        return value.ToString(CultureInfo.InvariantCulture).Trim();
    }

    private static List<List<string>> ReadCsv(byte[] content)
    {
        string text;
        using (var reader = new StreamReader(new MemoryStream(content), new UTF8Encoding(false, false), true))
        {
            text = reader.ReadToEnd();
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        // Excel in NZ/UK writes commas, in much of Europe semicolons; TSV uses tabs. The header row decides.
        var lineEnd = text.IndexOfAny(['\r', '\n']);
        var firstLine = lineEnd >= 0 ? text[..lineEnd] : text;
        var separator = new[] { ',', ';', '\t' }.MaxBy(d => firstLine.Count(ch => ch == d));

        var table = new List<List<string>>();
        using var csv = Sep.New(separator).Reader(o => o with
        {
            HasHeader = false,
            DisableColCountCheck = true,
            Unescape = true,
        }).FromText(text);
        foreach (var row in csv)
        {
            var cells = new List<string>(row.ColCount);
            for (var c = 0; c < row.ColCount; c++)
            {
                cells.Add(row[c].ToString().Trim());
            }

            table.Add(cells);
        }

        return table;
    }

    /// <summary>
    /// Drops empty rows and trailing empty columns, names blank headers "Column N", and de-duplicates header names
    /// case-insensitively ("Email", "Email (2)").
    /// </summary>
    private static SheetTable Normalize(List<List<string>> raw)
    {
        var table = raw.Where(r => r.Any(c => c.Length > 0)).ToList();
        if (table.Count == 0)
        {
            return SheetTable.Empty;
        }

        var width = table.Max(r => r.Count);
        while (width > 0 && table.All(r => Cell(r, width - 1).Length == 0))
        {
            width--;
        }

        var headers = new List<string>(width);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < width; i++)
        {
            var header = Cell(table[0], i).Trim();
            if (header.Length == 0)
            {
                header = $"Column {i + 1}";
            }

            var candidate = header;
            for (var n = 2; seen.Contains(candidate); n++)
            {
                candidate = $"{header} ({n})";
            }

            seen.Add(candidate);
            headers.Add(candidate);
        }

        var rows = table.Skip(1)
            .Select(r => (IReadOnlyList<string>)Enumerable.Range(0, width).Select(i => Cell(r, i)).ToList())
            .ToList();
        return new SheetTable(headers, rows);
    }

    private static string Cell(List<string> row, int index) => index < row.Count ? row[index] : "";
}
