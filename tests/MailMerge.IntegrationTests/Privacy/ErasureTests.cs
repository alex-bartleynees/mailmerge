using System.Net;
using System.Net.Http.Json;
using MailMerge.Api.Features.Campaigns;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MailMerge.IntegrationTests.Infrastructure.Delivery;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Privacy;

[Collection(ApiCollection.Name)]
public sealed class ErasureTests(MailMergeApiFactory api)
{
    private sealed record ListEntry(long ListId, string ListName);

    private sealed record CampaignEntry(long CampaignId, string CampaignName, string Status, bool DraftInMailbox);

    private sealed record PersonDto(string Email, List<ListEntry> Lists, List<CampaignEntry> Campaigns, bool OptedOut);

    private sealed record DraftDto(string CampaignName, string? Account);

    private sealed record ErasedDto(int ContactsDeleted, int MessagesDeleted, int ImportsDeleted, bool KeptOptedOut, List<DraftDto> DraftsToDeleteInOutlook);

    private sealed record LogDto(string Action, string PerformedBy, Dictionary<string, int> Counts);

    [Fact]
    public async Task Erasing_removes_the_person_everywhere_and_keeps_them_opted_out()
    {
        var client = api.CreateStaffClient();
        await CreateTestAccountAsync(client);
        var first = await CreateListAsync(client, Person("ann@example.com", "Ann"), Person("bob@example.com", "Bob"));
        var second = await CreateListAsync(client, Person("Ann@Example.com", "Annie"));
        var campaign = await CreateCampaignAsync(client, "Hi", "Hello");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { listId = first.Id });
        await RunAndWaitAsync(client, campaign.Id, "draft");
        // She then asks to stop emails (opt-out holds her address), and later to be forgotten.
        await client.PostAsync<object>("/api/opt-outs", new { emails = "ann@example.com" });

        var found = await client.GetAsync<PersonDto>("/api/privacy/people?email=ANN@example.com");
        found.Lists.Select(l => l.ListId).Should().BeEquivalentTo([first.Id, second.Id]);
        found.Campaigns.Should().ContainSingle().Which.DraftInMailbox.Should().BeTrue();

        var erased = await client.PostAsync<ErasedDto>("/api/privacy/people/erase", new { email = "ann@example.com" });

        erased.ContactsDeleted.Should().Be(2);
        erased.MessagesDeleted.Should().Be(1);
        erased.KeptOptedOut.Should().BeTrue();
        erased.DraftsToDeleteInOutlook.Should().ContainSingle().Which.CampaignName.Should().Be("Spring");

        var after = await client.GetAsync<PersonDto>("/api/privacy/people?email=ann@example.com");
        after.Lists.Should().BeEmpty();
        after.Campaigns.Should().BeEmpty();
        after.OptedOut.Should().BeTrue("they stay blocked by hash");
        var optOut = (await client.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().ContainSingle().Subject;
        optOut.Email.Should().BeNull("the plain address is gone");
        optOut.Source.Should().Be("erasure");

        var log = await client.GetAsync<List<LogDto>>("/api/privacy/log");
        log.Should().ContainSingle(l => l.Action == "erase").Which.Counts["contacts"].Should().Be(2);
        (await client.GetStringAsync("/api/privacy/log")).Should().NotContain("ann@");

        // An old spreadsheet re-adds her, but she's still never emailed.
        var readded = await client.PostAsync<ContactDto>($"/api/lists/{first.Id}/contacts", new { data = Person("ann@example.com", "Ann") });
        readded.OptedOut.Should().BeTrue();
        var plan = await client.GetAsync<PlanResponseDto>($"/api/campaigns/{campaign.Id}/plan");
        plan.Plan.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Erasing_without_keeping_the_opt_out_removes_it()
    {
        var client = api.CreateStaffClient();
        await client.PostAsync<object>("/api/opt-outs", new { emails = "dan@example.com" });

        await client.PostAsync<ErasedDto>("/api/privacy/people/erase", new { email = "dan@example.com", keepOptedOut = false });

        (await client.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().BeEmpty();
    }

    [Fact]
    public async Task Erasure_is_refused_while_the_person_is_being_emailed()
    {
        var client = api.CreateStaffClient();
        await CreateTestAccountAsync(client);
        await CreateListAsync(client, Person("eve@example.com"));
        var campaign = await CreateCampaignAsync(client, "Hi", "Hello");
        await RunAndWaitAsync(client, campaign.Id, "draft");
        await api.WithDbAsync(db => db.Messages.IgnoreQueryFilters().Where(m => m.CampaignId == campaign.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatus.Sending)));

        var response = await client.PostAsJsonAsync("/api/privacy/people/erase", new { email = "eve@example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.ProblemDetailAsync()).Should().Contain("being emailed right now");
        await api.WithDbAsync(db => db.Messages.IgnoreQueryFilters().Where(m => m.CampaignId == campaign.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatus.Drafted)));
    }

    [Fact]
    public async Task Erasure_only_affects_the_current_workspace()
    {
        var shopA = api.CreateStaffClient();
        var shopB = api.CreateStaffClient();
        await CreateListAsync(shopA, Person("same@example.com"));
        var listB = await CreateListAsync(shopB, Person("same@example.com"));

        await shopA.PostAsync<ErasedDto>("/api/privacy/people/erase", new { email = "same@example.com" });

        (await shopB.GetAsync<ListContactsDto>($"/api/lists/{listB.Id}/contacts")).Contacts.Should().ContainSingle();
        (await shopB.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().BeEmpty();
    }
}
