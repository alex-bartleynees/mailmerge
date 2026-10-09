using System.Text;
using ClosedXML.Excel;
using MailMerge.Api.Features.Imports;

namespace MailMerge.UnitTests.Imports;

public sealed class SpreadsheetReaderTests
{
    internal static byte[] Xlsx(string sheet, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(sheet);
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                ws.Cell(r + 1, c + 1).Value = rows[r][c] switch
                {
                    null => Blank.Value,
                    string s => s,
                    int i => i,
                    double d => d,
                    DateTime dt => dt,
                    _ => rows[r][c]!.ToString(),
                };
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    [Fact]
    public void Reads_headers_and_rows_from_the_chosen_sheet()
    {
        var bytes = Xlsx("Clients",
            ["First Name", "Email Address", "Phone", "Joined"],
            ["Ann", "ann@example.com", 21234567, new DateTime(2024, 3, 5)],
            ["Bob", "bob@example.com", 1.5, null]);

        SpreadsheetReader.SheetNames(bytes, "c.xlsx").Should().Equal("Clients");
        var table = SpreadsheetReader.Read(bytes, "c.xlsx", "Clients");

        table.Headers.Should().Equal("First Name", "Email Address", "Phone", "Joined");
        table.Rows[0].Should().Equal("Ann", "ann@example.com", "21234567", "2024-03-05");
        table.Rows[1].Should().Equal("Bob", "bob@example.com", "1.5", "");
    }

    [Fact]
    public void Drops_empty_rows_and_trailing_columns_and_fixes_headers()
    {
        var bytes = Xlsx("S",
            ["Email", null, "email", null, null],
            [null, null, null, null, null],
            ["a@x.com", "1", "b@x.com", null, null]);

        var table = SpreadsheetReader.Read(bytes, "c.xlsx");

        table.Headers.Should().Equal("Email", "Column 2", "email (2)");
        table.Rows.Should().ContainSingle().Which.Should().Equal("a@x.com", "1", "b@x.com");
    }

    [Theory]
    [InlineData("Name;E-mail\nZed;zed@example.com\n", "Zed")]
    [InlineData("Name,E-mail\r\n\"Smith, Zed\",zed@example.com\r\n", "Smith, Zed")]
    [InlineData("Name\tE-mail\nZed\tzed@example.com", "Zed")]
    public void Csv_separator_is_detected_from_the_header(string csv, string name)
    {
        var table = SpreadsheetReader.Read(Encoding.UTF8.GetBytes(csv), "x.csv");

        table.Headers.Should().Equal("Name", "E-mail");
        table.Rows.Should().ContainSingle().Which.Should().Equal(name, "zed@example.com");
    }

    [Fact]
    public void Csv_with_bom_and_ragged_rows_is_padded()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Email,Name,Town\nann@x.com\n")).ToArray();

        var table = SpreadsheetReader.Read(bytes, "x.csv");

        table.Headers[0].Should().Be("Email");
        table.Rows[0].Should().Equal("ann@x.com", "", "");
    }

    [Fact]
    public void Garbage_xlsx_gives_a_friendly_error()
    {
        var act = () => SpreadsheetReader.Read("garbage"u8.ToArray(), "x.xlsx");

        act.Should().Throw<SpreadsheetException>().WithMessage("Couldn't open that file*");
    }

    [Theory]
    [InlineData("a.xlsx", true)]
    [InlineData("a.XLSM", true)]
    [InlineData("a.csv", true)]
    [InlineData("a.xls", false)]
    [InlineData("a.pdf", false)]
    public void Supported_extensions(string name, bool supported) =>
        SpreadsheetReader.IsSupported(name).Should().Be(supported);
}
