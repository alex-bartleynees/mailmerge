using MailMerge.Api.Features.Campaigns;

namespace MailMerge.UnitTests.Merging;

public sealed class RecipientFilterTests
{
    private static Dictionary<string, string> Row(string rsvp, string send) =>
        new() { ["Email"] = "a@example.com", ["RSVP"] = rsvp, ["SEND_EMAIL"] = send };

    private static RecipientFilter BothYes(FilterMatch match) => new(match,
    [
        new FilterCondition("RSVP", FilterOperator.IsYes),
        new FilterCondition("SEND_EMAIL", FilterOperator.IsYes),
    ]);

    [Theory]
    [InlineData("TRUE")]
    [InlineData("true")]
    [InlineData(" Yes ")]
    [InlineData("y")]
    [InlineData("1")]
    [InlineData("x")]
    [InlineData("✓")]
    public void Spreadsheet_yes_values_count_as_yes(string value) => RecipientFilter.IsYes(value).Should().BeTrue();

    [Theory]
    [InlineData("")]
    [InlineData("FALSE")]
    [InlineData("no")]
    [InlineData("0")]
    [InlineData("maybe")]
    public void Anything_else_is_no(string value) => RecipientFilter.IsYes(value).Should().BeFalse();

    [Theory]
    [InlineData("TRUE", "TRUE", true)]
    [InlineData("TRUE", "FALSE", false)]
    [InlineData("", "yes", false)]
    public void All_needs_every_condition(string rsvp, string send, bool expected) =>
        BothYes(FilterMatch.All).Matches(Row(rsvp, send)).Should().Be(expected);

    [Theory]
    [InlineData("TRUE", "FALSE", true)]
    [InlineData("", "yes", true)]
    [InlineData("no", "", false)]
    public void Any_needs_one_condition(string rsvp, string send, bool expected) =>
        BothYes(FilterMatch.Any).Matches(Row(rsvp, send)).Should().Be(expected);

    [Fact]
    public void Columns_match_case_insensitively_and_missing_columns_are_blank()
    {
        var data = new Dictionary<string, string> { ["Tier"] = "Gold" };

        new RecipientFilter(FilterMatch.All, [new FilterCondition("tier", FilterOperator.Equals, "gold")])
            .Matches(data).Should().BeTrue();
        new RecipientFilter(FilterMatch.All, [new FilterCondition("Missing", FilterOperator.IsEmpty)])
            .Matches(data).Should().BeTrue();
        new RecipientFilter(FilterMatch.All, [new FilterCondition("Missing", FilterOperator.IsYes)])
            .Matches(data).Should().BeFalse();
    }

    [Theory]
    [InlineData(FilterOperator.Equals, "Gold", true)]
    [InlineData(FilterOperator.NotEquals, "Gold", false)]
    [InlineData(FilterOperator.Contains, "ol", true)]
    [InlineData(FilterOperator.IsEmpty, null, false)]
    [InlineData(FilterOperator.IsNotEmpty, null, true)]
    [InlineData(FilterOperator.IsNo, null, true)]
    public void Operators(FilterOperator op, string? value, bool expected) =>
        new RecipientFilter(FilterMatch.All, [new FilterCondition("Tier", op, value)])
            .Matches(new Dictionary<string, string> { ["Tier"] = "Gold" }).Should().Be(expected);

    [Fact]
    public void An_empty_filter_matches_everyone() =>
        new RecipientFilter(FilterMatch.All, []).Matches(Row("", "")).Should().BeTrue();
}
