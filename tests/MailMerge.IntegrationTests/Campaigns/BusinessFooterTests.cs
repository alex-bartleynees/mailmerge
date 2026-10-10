using System.Net;
using System.Net.Http.Json;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Features.Workspaces.GetCurrent;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static MailMerge.IntegrationTests.Infrastructure.Delivery;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Campaigns;

[Collection(ApiCollection.Name)]
public sealed class BusinessFooterTests(MailMergeApiFactory api)
{
    [Fact]
    public async Task Workspace_default_is_optional_and_only_used_when_the_email_opts_in()
    {
        var client = api.CreateStaffClient();
        var initial = await client.GetAsync<GetCurrentWorkspaceResponse>("/api/workspace");
        initial.Workspace.BusinessFooter.Should().BeNull();
        await CreateListAsync(client, Person("ann@example.com"));
        var campaign = await CreateCampaignAsync(client, "News", "Hello", isHtml: true);
        campaign.IncludeBusinessFooter.Should().BeFalse();
        campaign.UseWorkspaceBusinessFooter.Should().BeTrue();

        await client.PutAsync<GetCurrentWorkspaceResponse>("/api/workspace",
            new { businessFooter = new { html = "<b>Example Business</b><br>hello@example.com" } });
        (await Preview(client, campaign.Id)).Body.Should().Be("Hello");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { includeBusinessFooter = true });
        (await Preview(client, campaign.Id)).Body.Should().Contain("<b>Example Business</b>");

        await client.PutAsync<GetCurrentWorkspaceResponse>("/api/workspace", new { businessFooter = new { html = "Updated business" } });
        (await Preview(client, campaign.Id)).Body.Should().Contain("Updated business");
        await client.PutAsync<GetCurrentWorkspaceResponse>("/api/workspace", new { name = "Renamed" });
        (await client.GetAsync<GetCurrentWorkspaceResponse>("/api/workspace")).Workspace.BusinessFooter!.Html
            .Should().Be("Updated business", "omitting the field preserves the default");

        var other = api.CreateStaffClient();
        var otherCampaign = await CreateCampaignAsync(other, "Other", "Hello", isHtml: true);
        await other.PutAsync<CampaignDto>($"/api/campaigns/{otherCampaign.Id}", new { includeBusinessFooter = true });
        (await Preview(other, otherCampaign.Id)).Body.Should().Be("Hello", "defaults belong to one workspace");

        await client.PutAsync<GetCurrentWorkspaceResponse>("/api/workspace", new { businessFooter = new { html = "", logoUrl = "" } });
        (await Preview(client, campaign.Id)).Body.Should().Be("Hello", "a default can be cleared");
    }

    [Fact]
    public async Task Custom_footer_appears_in_preview_test_and_delivery_and_is_copied()
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        await CreateListAsync(client, Person("ann@example.com"));
        await client.PutAsync<GetCurrentWorkspaceResponse>("/api/workspace", new { businessFooter = new { html = "Workspace business" } });
        var campaign = await CreateCampaignAsync(client, "News", "<p>Hello</p>", isHtml: true);
        var updated = await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new
        {
            includeBusinessFooter = true, useWorkspaceBusinessFooter = false, unsubscribeFooter = true,
            businessFooter = new { html = "<b>Custom business</b><br>Phone: 123", logoUrl = "https://example.com/logo.png" },
        });
        var preview = await Preview(client, campaign.Id);
        preview.Body.Should().Contain("<b>Custom business</b>").And.Contain("src=\"https://example.com/logo.png\"")
            .And.NotContain("Workspace business");
        preview.Body.IndexOf("Custom business", StringComparison.Ordinal).Should()
            .BeLessThan(preview.Body.IndexOf("Unsubscribe</a>", StringComparison.Ordinal));

        var copy = await (await client.PostAsync($"/api/campaigns/{campaign.Id}/copy", null)).ReadAsync<CampaignDto>();
        copy.IncludeBusinessFooter.Should().BeTrue();
        copy.UseWorkspaceBusinessFooter.Should().BeFalse();
        copy.BusinessFooter.Should().BeEquivalentTo(updated.BusinessFooter);

        var test = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/test", new { to = "staff@example.com" });
        test.StatusCode.Should().Be(HttpStatusCode.OK);
        await RunAndWaitAsync(client, campaign.Id, "send");
        var sent = api.Services.GetRequiredService<TestMailbox>().For(account.Id).Sent;
        sent.Should().HaveCount(2);
        foreach (var message in sent)
        {
            message.HtmlBody.Should().Contain("<b>Custom business</b>").And.Contain("https://example.com/logo.png");
        }

        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { includeBusinessFooter = false });
        (await Preview(client, campaign.Id)).Body.Should().NotContain("Custom business").And.Contain("Unsubscribe</a>");
    }

    [Fact]
    public async Task Plain_text_email_uses_text_details_without_a_logo()
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        await CreateListAsync(client, Person("ann@example.com"));
        var campaign = await CreateCampaignAsync(client, "News", "Hello");
        await client.PutAsync<GetCurrentWorkspaceResponse>("/api/workspace", new
        {
            businessFooter = new { html = "<b>A&amp;B Business</b><br>hello@example.com", logoUrl = "https://example.com/logo.png" },
        });
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { includeBusinessFooter = true });
        (await Preview(client, campaign.Id)).Body.Should().Be("Hello\n\n--\nA&B Business\nhello@example.com\n");
        await RunAndWaitAsync(client, campaign.Id, "send");
        api.Services.GetRequiredService<TestMailbox>().For(account.Id).Sent.Single().TextBody
            .Should().Contain("A&B Business").And.NotContain("logo.png");
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,abc")]
    [InlineData("/local/logo.png")]
    public async Task Logo_requires_a_web_url_in_workspace_and_campaign(string url)
    {
        var client = api.CreateStaffClient();
        var campaign = await CreateCampaignAsync(client, "News", "Hello");
        var body = new { businessFooter = new { html = "Business", logoUrl = url } };
        (await client.PutAsJsonAsync("/api/workspace", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", body)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Task<PreviewDto> Preview(HttpClient client, long id) => client.GetAsync<PreviewDto>($"/api/campaigns/{id}/preview");
}
