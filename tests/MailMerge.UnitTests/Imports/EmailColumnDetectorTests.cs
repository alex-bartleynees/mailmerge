using MailMerge.Api.Features.Imports;

namespace MailMerge.UnitTests.Imports;

public sealed class EmailColumnDetectorTests
{
    private static SheetTable Table(string[] headers, params string[][] rows) =>
        new(headers, rows.Select(r => (IReadOnlyList<string>)r).ToList());

    [Theory]
    [InlineData("Email Address")]
    [InlineData("E-mail")]
    [InlineData("email_addr")]
    public void Recognises_common_header_names(string header) =>
        EmailColumnDetector.Detect(Table(["Name", header], ["Ann", "x"])).Should().Be(header);

    [Fact]
    public void Falls_back_to_a_header_mentioning_mail() =>
        EmailColumnDetector.Detect(Table(["Name", "Work mail box"], ["Ann", "x"])).Should().Be("Work mail box");

    [Fact]
    public void Falls_back_to_the_column_whose_values_look_like_addresses() =>
        EmailColumnDetector.Detect(Table(["Name", "Contact"],
                ["Ann", "ann@example.com"], ["Bob", "bob@example.com"], ["Cy", "021 123"]))
            .Should().Be("Contact");

    [Fact]
    public void Returns_empty_when_nothing_qualifies() =>
        EmailColumnDetector.Detect(Table(["Name", "Phone"], ["Ann", "021"])).Should().BeEmpty();
}
