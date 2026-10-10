using MailMerge.IntegrationTests.Infrastructure;

namespace MailMerge.IntegrationTests.Campaigns;

[Collection(ApiCollection.Name)]
public sealed class HtmlSafetyTests(MailMergeApiFactory api)
{
    private const string Malicious = "<p>Hi {{First Name}}</p><img src=x onerror=alert(1)><a href=\"javascript:alert(1)\">x</a>";

    private sealed record FooterDto(string Html, string? LogoUrl);

    private sealed record CampaignHtmlDto(long Id, string Body, bool IsHtml, FooterDto? BusinessFooter);

    private sealed record WorkspaceDto(FooterDto? BusinessFooter);

    private sealed record CurrentDto(WorkspaceDto Workspace);

    [Fact]
    public async Task Formatted_bodies_and_campaign_footers_are_sanitised_when_saved()
    {
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "x" });

        var saved = await client.PutAsync<CampaignHtmlDto>($"/api/campaigns/{campaign.Id}", new
        {
            isHtml = true,
            body = Malicious,
            businessFooter = new { html = Malicious },
        });

        saved.Body.Should().Contain("Hi {{First Name}}").And.NotContain("onerror").And.NotContain("javascript:");
        saved.BusinessFooter!.Html.Should().NotContain("onerror").And.NotContain("javascript:");
    }

    [Fact]
    public async Task Plain_text_bodies_are_stored_as_typed()
    {
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "x" });

        var saved = await client.PutAsync<CampaignHtmlDto>($"/api/campaigns/{campaign.Id}",
            new { isHtml = false, body = "Use <b> for bold & > for quotes" });

        saved.Body.Should().Be("Use <b> for bold & > for quotes", "plain text is never rendered as HTML");
    }

    [Fact]
    public async Task Switching_an_existing_body_to_formatted_sanitises_it()
    {
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "x" });
        await client.PutAsync<CampaignHtmlDto>($"/api/campaigns/{campaign.Id}", new { isHtml = false, body = Malicious });

        var switched = await client.PutAsync<CampaignHtmlDto>($"/api/campaigns/{campaign.Id}", new { isHtml = true });

        switched.Body.Should().NotContain("onerror");
    }

    [Fact]
    public async Task The_shop_footer_is_sanitised_when_saved()
    {
        var client = api.CreateStaffClient();

        var updated = await client.PutAsync<CurrentDto>("/api/workspace", new { businessFooter = new { html = Malicious } });

        updated.Workspace.BusinessFooter!.Html.Should().NotContain("onerror").And.NotContain("javascript:");
    }
}
