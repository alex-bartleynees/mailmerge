using System.Net;
using System.Net.Http.Json;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using static MailMerge.IntegrationTests.Infrastructure.Delivery;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Campaigns;

/// <summary>"Only send to people where RSVP is yes and/or SEND_EMAIL is yes", from spreadsheet columns.</summary>
[Collection(ApiCollection.Name)]
public sealed class RecipientFilterTests(MailMergeApiFactory api)
{
    private static Dictionary<string, string> Guest(string email, string rsvp, string send) =>
        new() { ["Email"] = email, ["RSVP"] = rsvp, ["SEND_EMAIL"] = send };

    private static object Filter(string match) => new
    {
        match,
        conditions = new[]
        {
            new { column = "RSVP", @operator = "isYes", value = (string?)null },
            new { column = "SEND_EMAIL", @operator = "isYes", value = (string?)null },
        },
    };

    [Fact]
    public async Task Both_yes_then_either_yes_only_reaches_newly_included_people()
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        await CreateListAsync(client,
            Guest("both@example.com", "TRUE", "TRUE"),
            Guest("rsvp-only@example.com", "TRUE", "FALSE"),
            Guest("send-only@example.com", "FALSE", "yes"),
            Guest("neither@example.com", "", ""),
            Guest("ones@example.com", "1", "x"));
        var campaign = await CreateCampaignAsync(client, "Event", "See you there");

        var withFilter = await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { filter = Filter("all") });
        withFilter.Filter!.Conditions.Should().HaveCount(2);

        var plan = (await client.GetAsync<PlanResponseDto>($"/api/campaigns/{campaign.Id}/plan")).Plan;
        plan.ToSend.Should().Be(2);
        plan.FilteredOut.Should().Be(3);

        var preview = await client.GetAsync<PreviewDto>($"/api/campaigns/{campaign.Id}/preview");
        preview.Total.Should().Be(5);
        preview.Matching.Should().Be(2);
        preview.To.Should().Be("both@example.com");
        var next = await client.GetAsync<PreviewDto>($"/api/campaigns/{campaign.Id}/preview?contactId={preview.NextId}");
        next.To.Should().Be("ones@example.com", "paging skips people the filter excludes");
        next.NextId.Should().BeNull();

        await RunAndWaitAsync(client, campaign.Id, "send");
        Sent(account.Id).Should().BeEquivalentTo("both@example.com", "ones@example.com");
        (await MessagesAsync(client, campaign.Id)).Single(m => m.Email == "neither@example.com").Error
            .Should().Be("Not included by this email's filter");

        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { filter = Filter("any") });
        await RunAndWaitAsync(client, campaign.Id, "send");

        Sent(account.Id).Should().BeEquivalentTo(
            "both@example.com", "ones@example.com", "rsvp-only@example.com", "send-only@example.com");
        Sent(account.Id).Should().HaveCount(4, "people already emailed aren't emailed again");
    }

    [Fact]
    public async Task Empty_filter_clears_it_and_copies_keep_it()
    {
        var client = api.CreateStaffClient();
        await CreateListAsync(client, Guest("a@example.com", "TRUE", "TRUE"));
        var campaign = await CreateCampaignAsync(client, "S", "B");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { filter = Filter("any") });

        var copy = await (await client.PostAsync($"/api/campaigns/{campaign.Id}/copy", null)).ReadAsync<CampaignDto>();
        copy.Filter!.Match.Should().Be("any");

        var cleared = await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}",
            new { filter = new { match = "all", conditions = Array.Empty<object>() } });
        cleared.Filter.Should().BeNull();
    }

    [Fact]
    public async Task Invalid_conditions_are_rejected_with_a_readable_message()
    {
        var client = api.CreateStaffClient();
        var campaign = await CreateCampaignAsync(client, "S", "B");

        var noValue = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", new
        {
            filter = new { match = "all", conditions = new[] { new { column = "Tier", @operator = "equals", value = "" } } },
        });
        var noColumn = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", new
        {
            filter = new { match = "all", conditions = new[] { new { column = "", @operator = "isYes", value = "" } } },
        });

        noValue.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noValue.ProblemDetailAsync()).Should().Be("Enter the value to compare with");
        (await noColumn.ProblemDetailAsync()).Should().Be("Choose a column for each filter condition");
    }

    private IEnumerable<string> Sent(long accountId) =>
        api.Services.GetRequiredService<TestMailbox>().For(accountId).Sent.Select(m => m.To.ToString());

    [Theory]
    [InlineData("{\"filter\":{\"match\":\"all\",\"conditions\":null}}")]
    [InlineData("{\"filter\":{\"match\":\"all\",\"conditions\":[null]}}")]
    public async Task Null_conditions_return_validation_errors(string json)
    {
        var client = api.CreateStaffClient();
        var campaign = await CreateCampaignAsync(client, "S", "B");
        var response = await client.PutAsync($"/api/campaigns/{campaign.Id}",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unknown_columns_and_switching_to_a_list_without_filter_columns_are_rejected()
    {
        var client = api.CreateStaffClient();
        var list = await CreateListAsync(client, Guest("a@example.com", "TRUE", "TRUE"));
        var campaign = await CreateCampaignAsync(client, "S", "B");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { filter = Filter("all") });
        var otherList = await CreateListAsync(client, Person("b@example.com"));

        var changed = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", new { listId = otherList.Id });
        changed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}")).ListId.Should().Be(list.Id);

        var unknown = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", new
        {
            filter = new { match = "all", conditions = new[] { new { column = "Does not exist", @operator = "isNo" } } },
        });
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.ProblemDetailAsync()).Should().Contain("Does not exist");
    }

    [Theory]
    [InlineData("isNo", null)]
    [InlineData("isEmpty", null)]
    [InlineData("notEquals", "TRUE")]
    public async Task Deleted_columns_block_planning_and_delivery(string op, string? value)
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        var list = await CreateListAsync(client, Guest("a@example.com", "FALSE", "FALSE"));
        var campaign = await CreateCampaignAsync(client, "S", "B");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new
        {
            filter = new { match = "all", conditions = new[] { new { column = "SEND_EMAIL", @operator = op, value } } },
        });
        (await client.DeleteAsync($"/api/lists/{list.Id}/columns/SEND_EMAIL")).EnsureSuccessStatusCode();

        var plan = await client.GetAsync<System.Text.Json.JsonElement>($"/api/campaigns/{campaign.Id}/plan");
        plan.GetProperty("filterError").GetString().Should().Contain("SEND_EMAIL");
        plan.GetProperty("plan").GetProperty("toSend").GetInt32().Should().Be(0);
        var run = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/run", new { mode = "send" });
        run.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        Sent(account.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task Excel_booleans_with_custom_headers_drive_actual_delivery()
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        var bytes = TestData.Xlsx("Guests",
            ["Email address", "Attending?", "Send invitation"],
            ["both@example.com", true, true],
            ["one@example.com", true, false],
            ["other@example.com", false, true],
            ["none@example.com", false, false]);
        var preview = await (await client.PostAsync("/api/imports", TestData.File("guests.xlsx", bytes)))
            .ReadAsync<ImportPreviewDto>();
        var imported = await client.PostAsync<CommitImportDto>($"/api/imports/{preview.Token}/commit",
            new { emailColumn = "Email address", newListName = "Guests" });
        var campaign = await CreateCampaignAsync(client, "Hello", "Welcome");
        var conditions = new[]
        {
            new { column = "Attending?", @operator = "isYes" },
            new { column = "Send invitation", @operator = "isYes" },
        };
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}",
            new { listId = imported.ListId, filter = new { match = "all", conditions } });
        await RunAndWaitAsync(client, campaign.Id, "send");
        Sent(account.Id).Should().Equal("both@example.com");

        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { filter = new { match = "any", conditions } });
        await RunAndWaitAsync(client, campaign.Id, "send");
        Sent(account.Id).Should().BeEquivalentTo("both@example.com", "one@example.com", "other@example.com");
    }

    [Fact]
    public async Task Removing_a_rule_column_after_starting_pauses_delivery_before_any_send()
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        // Hold delivery at the account's pacing gate while changing the list.
        await api.WithDbAsync(db => db.SendingAccounts.IgnoreQueryFilters().Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.NextSendAt, DateTimeOffset.UtcNow.AddHours(1))));
        var list = await CreateListAsync(client, Guest("a@example.com", "FALSE", "FALSE"));
        var campaign = await CreateCampaignAsync(client, "S", "B");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new
        {
            filter = new { match = "all", conditions = new[] { new { column = "SEND_EMAIL", @operator = "isNo" } } },
        });
        (await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/run", new { mode = "send" })).EnsureSuccessStatusCode();
        (await client.DeleteAsync($"/api/lists/{list.Id}/columns/SEND_EMAIL")).EnsureSuccessStatusCode();
        await api.WithDbAsync(db => db.SendingAccounts.IgnoreQueryFilters().Where(a => a.Id == account.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.NextSendAt, (DateTimeOffset?)null)));

        var paused = await WaitAsync(client, campaign.Id);
        paused.Status.Should().Be("paused");
        paused.Note.Should().Contain("SEND_EMAIL");
        Sent(account.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task Switching_lists_does_not_send_previous_lists_drafts()
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        await CreateListAsync(client, Guest("old@example.com", "TRUE", "TRUE"));
        var campaign = await CreateCampaignAsync(client, "S", "B");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { filter = Filter("all") });
        await RunAndWaitAsync(client, campaign.Id, "draft");

        var next = await CreateListAsync(client, Guest("new@example.com", "TRUE", "TRUE"));
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { listId = next.Id });
        await RunAndWaitAsync(client, campaign.Id, "send");
        Sent(account.Id).Should().Equal("new@example.com");
    }
}
