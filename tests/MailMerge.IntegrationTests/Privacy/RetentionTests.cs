using System.Net;
using System.Net.Http.Json;
using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Features.Privacy.Retention;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MailMerge.IntegrationTests.Infrastructure.Delivery;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Privacy;

[Collection(ApiCollection.Name)]
public sealed class RetentionTests(MailMergeApiFactory api)
{
    private sealed record WorkspaceView(long Id, string Name, int RetentionDays);

    private sealed record CurrentDto(WorkspaceView Workspace);

    private sealed record LogDto(string Action, Dictionary<string, int> Counts);

    private Task Age(long campaignId, int days) => api.WithDbAsync(db => db.Campaigns.IgnoreQueryFilters()
        .Where(c => c.Id == campaignId)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.LastActivityAt, c => c.LastActivityAt.AddDays(-days))));

    private Task Purge() => api.Services.GetRequiredService<RetentionService>().PurgeAsync(CancellationToken.None);

    private async Task<(HttpClient Client, CampaignDto Campaign)> SentCampaign(params string[] emails)
    {
        var client = api.CreateStaffClient();
        await CreateTestAccountAsync(client);
        await CreateListAsync(client, emails.Select(e => Person(e)).ToArray());
        var campaign = await CreateCampaignAsync(client, "Hi", "Hello");
        await client.PostAsync($"/api/campaigns/{campaign.Id}/attachments", TestData.File("a.pdf", [1, 2, 3]));
        await RunAndWaitAsync(client, campaign.Id, "send");
        return (client, campaign);
    }

    [Fact]
    public async Task Old_campaigns_are_archived_keeping_a_summary()
    {
        var (client, campaign) = await SentCampaign("ann@example.com", "bob@example.com");
        (await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}")).Counts["sent"].Should().Be(2);
        await Age(campaign.Id, 91);

        await Purge();

        var archived = await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}");
        archived.ArchivedAt.Should().NotBeNull();
        archived.Counts["sent"].Should().Be(2, "the summary survives");
        archived.Attachments.Should().ContainSingle().Which.Available.Should().BeFalse();
        (await MessagesAsync(client, campaign.Id)).Should().BeEmpty();

        var run = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/run", new { mode = "send" });
        run.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await run.ProblemDetailAsync()).Should().Contain("Copy as new email");
        var copy = await (await client.PostAsync($"/api/campaigns/{campaign.Id}/copy", null)).ReadAsync<CampaignDto>();
        copy.Subject.Should().Be("Hi");

        var log = await client.GetAsync<List<LogDto>>("/api/privacy/log");
        log.Should().ContainSingle(l => l.Action == "retention_purge").Which.Counts["messages"].Should().Be(2);
    }

    [Fact]
    public async Task Recent_and_unresolved_campaigns_are_kept()
    {
        var (client, recent) = await SentCampaign("ann@example.com");
        var (otherClient, unresolved) = await SentCampaign("bob@example.com");
        await api.WithDbAsync(db => db.Messages.IgnoreQueryFilters().Where(m => m.CampaignId == unresolved.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatus.Uncertain)));
        await Age(recent.Id, 30);
        await Age(unresolved.Id, 400);

        await Purge();

        (await client.GetAsync<CampaignDto>($"/api/campaigns/{recent.Id}")).ArchivedAt.Should().BeNull();
        var kept = await otherClient.GetAsync<CampaignDto>($"/api/campaigns/{unresolved.Id}");
        kept.ArchivedAt.Should().BeNull("someone still has to check whether it was sent");
        (await client.GetAsync<CampaignDto>($"/api/campaigns/{recent.Id}")).Should().NotBeNull();
    }

    [Fact]
    public async Task Retention_period_is_a_workspace_setting()
    {
        var (client, campaign) = await SentCampaign("ann@example.com");

        var updated = await client.PutAsync<CurrentDto>("/api/workspace", new { name = "Max Wilson Jewellers", retentionDays = 30 });
        updated.Workspace.Should().BeEquivalentTo(new { Name = "Max Wilson Jewellers", RetentionDays = 30 });
        (await client.PutAsJsonAsync("/api/workspace", new { retentionDays = 1 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await Age(campaign.Id, 31);
        await Purge();

        (await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}")).ArchivedAt.Should().NotBeNull();
    }
}
