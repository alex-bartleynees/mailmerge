using MailMerge.Api.Shared.Merging;

namespace MailMerge.UnitTests.Merging;

public sealed class SafeHtmlTests
{
    [Theory]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("<p onclick=\"alert(1)\">hi</p>")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<a href=\"javascript:alert(1)\">x</a>")]
    [InlineData("<a href=\" JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<iframe src=\"https://evil.test\"></iframe>")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("<form action=\"https://evil.test\"><input name=p></form>")]
    [InlineData("<div style=\"background:url(javascript:alert(1))\">x</div>")]
    public void Strips_anything_that_could_run_script(string html)
    {
        var clean = SafeHtml.Clean(html).ToLowerInvariant();

        clean.Should().NotContain("onerror").And.NotContain("onclick").And.NotContain("onload")
            .And.NotContain("<script").And.NotContain("javascript:").And.NotContain("<iframe")
            .And.NotContain("<form").And.NotContain("<svg");
    }

    [Fact]
    public void Keeps_editor_formatting_and_safe_links()
    {
        const string html = "<p><b>Hi</b> <i>there</i> <u>you</u></p><ul><li>One</li></ul>" +
                            "<a href=\"https://shop.co.nz\">Shop</a> <a href=\"mailto:sales@shop.co.nz\">Email</a><br>";

        var clean = SafeHtml.Clean(html);

        clean.Should().Contain("<b>Hi</b>").And.Contain("<i>there</i>").And.Contain("<u>you</u>")
            .And.Contain("<li>One</li>").And.Contain("href=\"https://shop.co.nz\"")
            .And.Contain("href=\"mailto:sales@shop.co.nz\"");
    }

    [Fact]
    public void Merge_fields_survive_in_text_and_link_addresses()
    {
        var clean = SafeHtml.Clean("<p>Dear {{First Name|there}}, visit <a href=\"{{Website}}\">your page</a></p>");

        clean.Should().Contain("Dear {{First Name|there}}").And.Contain("href=\"{{Website}}\"");
    }

    [Fact]
    public void A_crafted_merge_tag_cannot_break_out_of_an_attribute()
    {
        var clean = SafeHtml.Clean("<a href=\"{{a\" onmouseover=\"alert(1)}}\">x</a>");

        // The quotes are encoded, so "onmouseover" stays inside the href value instead of becoming an attribute.
        clean.Should().Be("<a href=\"{{a&quot; onmouseover=&quot;alert(1)}}\">x</a>");
    }

    [Fact]
    public void Apostrophes_in_fallbacks_are_not_encoded() =>
        SafeHtml.Clean("<p>Hi {{Name|O'Brien}}</p>").Should().Contain("{{Name|O'Brien}}");
}
