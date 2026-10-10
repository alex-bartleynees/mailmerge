using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.UnitTests.Campaigns;

public sealed class MessageBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Business_footer_requires_opt_in_and_custom_content_overrides_the_default(bool isHtml)
    {
        var campaign = new Campaign { Name = "News", Subject = "Hello", Body = "Hi", IsHtml = isHtml };
        var workspace = new BusinessFooter("<b>Workspace business</b>", "https://example.com/logo.png");
        MessageBuilder.Body(campaign, new Dictionary<string, string>(), workspace, null).Should().Be("Hi");

        campaign.IncludeBusinessFooter = true;
        var body = MessageBuilder.Body(campaign, new Dictionary<string, string>(), workspace, null);
        body.Should().Contain("Workspace business");
        if (isHtml) body.Should().Contain("logo.png");
        else body.Should().NotContain("logo.png").And.NotContain("<b>");

        campaign.UseWorkspaceBusinessFooter = false;
        campaign.BusinessFooter = new BusinessFooter("Custom business");
        MessageBuilder.Body(campaign, new Dictionary<string, string>(), workspace, null)
            .Should().Contain("Custom business").And.NotContain("Workspace business");
        campaign.BusinessFooter = new BusinessFooter("");
        MessageBuilder.Body(campaign, new Dictionary<string, string>(), workspace, null).Should().Be("Hi");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Blank_message_keeps_the_existing_footer(string? message)
    {
        MessageBuilder.Footer("https://example.com/u/token", false, message)
            .Should().Be("\n\n--\nDon't want these emails? Unsubscribe here: https://example.com/u/token\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_footer_keeps_the_link_and_unsubscribe_headers(bool isHtml)
    {
        const string url = "https://example.com/u/token";
        var campaign = new Campaign
        {
            Name = "News", Subject = "Hello", Body = "Hi", IsHtml = isHtml,
            UnsubscribeFooter = true, UnsubscribeMessage = "Stop updates from A&B?\n<Leave>",
        };

        var message = MessageBuilder.Build(campaign, new Dictionary<string, string>(), "ann@example.com", [], url);

        message.UnsubscribeUrl.Should().Be(url);
        message.Body.Should().Contain(url).And.Contain("Unsubscribe");
        message.Body.Should().Contain(isHtml ? "A&amp;B?<br>&lt;Leave&gt;" : "A&B?\n<Leave>");
        campaign.UnsubscribeFooter = false;
        MessageBuilder.Build(campaign, new Dictionary<string, string>(), "ann@example.com", [], url)
            .Should().BeEquivalentTo(message with { Body = "Hi", UnsubscribeUrl = null });
    }
}
