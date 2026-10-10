using MailMerge.Api.Shared.Merging;

namespace MailMerge.UnitTests.Merging;

public sealed class MergeTemplateTests
{
    private static readonly Dictionary<string, string> Ann = new()
    {
        ["First Name"] = "Ann",
        ["Company"] = "Acme",
        ["Suburb"] = "",
    };

    [Fact]
    public void Replaces_fields_with_values_ignoring_case_and_spaces()
    {
        MergeTemplate.Render("Dear {{ first name }} at {{Company}}", Ann, asHtml: false)
            .Should().Be("Dear Ann at Acme");
    }

    [Fact]
    public void Blank_or_missing_values_use_the_fallback()
    {
        MergeTemplate.Render("{{Suburb|unknown}} / {{Nickname|friend}} / {{Missing}}", Ann, asHtml: false)
            .Should().Be("unknown / friend / ");
    }

    [Fact]
    public void Html_mode_escapes_contact_values_but_not_the_template()
    {
        var data = new Dictionary<string, string> { ["Name"] = "Fay <b>&" };

        MergeTemplate.Render("<p>Hi {{Name}}</p>", data, asHtml: true)
            .Should().Be("<p>Hi Fay &lt;b&gt;&amp;</p>");
        MergeTemplate.Render("Hi {{Name}}", data, asHtml: false).Should().Be("Hi Fay <b>&");
    }

    [Fact]
    public void Used_fields_lists_names_in_order()
    {
        MergeTemplate.UsedFields("Hi {{First Name|there}} at {{ Company }} {{Missing}}")
            .Should().Equal("First Name", "Company", "Missing");
    }

    [Fact]
    public void Html_to_text_keeps_line_breaks()
    {
        MergeTemplate.HtmlToText("<p>Hello&nbsp;<b>Ann</b></p><ul><li>One</li><li>Two</li></ul><br><br><br><br>Bye")
            .Should().Be("Hello Ann\nOne\nTwo\n\nBye");
    }
}

public sealed class MergeTemplateUrlTests
{
    private static string Link(string website) =>
        MergeTemplate.Render("<a href=\"{{Website}}\">site</a>",
            new Dictionary<string, string> { ["Website"] = website }, asHtml: true);

    [Theory]
    [InlineData("https://shop.co.nz/a?b=1", "https://shop.co.nz/a?b=1")]
    [InlineData("mailto:sales@shop.co.nz", "mailto:sales@shop.co.nz")]
    [InlineData("shop.co.nz/rings", "https://shop.co.nz/rings")]
    [InlineData("javascript:alert(1)", "")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=", "")]
    [InlineData("not a url", "")]
    public void A_field_starting_a_link_must_be_a_safe_url(string value, string expectedHref) =>
        Link(value).Should().Be($"<a href=\"{System.Net.WebUtility.HtmlEncode(expectedHref)}\">site</a>");

    [Fact]
    public void A_field_later_in_the_address_cannot_change_the_scheme_and_is_left_as_is() =>
        MergeTemplate.Render("<a href=\"https://shop.co.nz/?ref={{Id}}\">x</a>",
                new Dictionary<string, string> { ["Id"] = "javascript:1" }, asHtml: true)
            .Should().Be("<a href=\"https://shop.co.nz/?ref=javascript:1\">x</a>");

    [Fact]
    public void Text_outside_tags_is_unaffected() =>
        MergeTemplate.Render("<p>Visit {{Website}}</p>",
                new Dictionary<string, string> { ["Website"] = "javascript:alert(1)" }, asHtml: true)
            .Should().Be("<p>Visit javascript:alert(1)</p>");
}
