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
