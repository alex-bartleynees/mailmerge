using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.IntegrationTests.Campaigns;

/// <summary>Campaign and Message saves fail cleanly (xmin) if the delivery worker changed the row in between.</summary>
[Collection(ApiCollection.Name)]
public sealed class ConcurrencyTests(MailMergeApiFactory api)
{
    [Fact]
    public async Task A_stale_campaign_save_is_rejected_after_another_writer_updates_the_row()
    {
        var client = api.CreateStaffClient();
        var created = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "x" });

        await api.WithDbAsync(async db =>
        {
            var campaign = await db.Campaigns.IgnoreQueryFilters().SingleAsync(c => c.Id == created.Id);
            // Another writer (e.g. the worker's set-based update) changes the row after we loaded it.
            await api.WithDbAsync(other => other.Campaigns.IgnoreQueryFilters().Where(c => c.Id == created.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Note, "worker was here")));

            campaign.Subject = "stale edit";
            var save = () => db.SaveChangesAsync();
            await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
            return true;
        });

        (await client.GetAsync<CampaignDto>($"/api/campaigns/{created.Id}")).Note.Should().Be("worker was here");
    }

    [Fact]
    public async Task Pause_wins_its_race_with_the_worker()
    {
        var client = api.CreateStaffClient();
        await Delivery.CreateTestAccountAsync(client, perMinute: 1);
        await Scenario.CreateListAsync(client,
            Enumerable.Range(1, 5).Select(i => Scenario.Person($"p{i}@example.com")).ToArray());
        var campaign = await Scenario.CreateCampaignAsync(client, "Slow", "Hi");
        await client.PostAsync<object>($"/api/campaigns/{campaign.Id}/run", new { mode = "send" });

        (await client.PostAsync($"/api/campaigns/{campaign.Id}/pause", null)).IsSuccessStatusCode.Should().BeTrue();

        (await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}")).Status.Should().Be("paused");
    }
}
