using System.Net;
using MailMerge.IntegrationTests.Infrastructure;

namespace MailMerge.IntegrationTests.OptOuts;

[Collection(ApiCollection.Name)]
public sealed class OptOutTests(MailMergeApiFactory api)
{
    private sealed record AddedDto(int Added, int AlreadyOptedOut, List<string> Ignored);

    [Fact]
    public async Task Added_addresses_are_listed_and_flag_matching_contacts()
    {
        var client = api.CreateStaffClient();
        var list = await client.PostAsync<ListDto>("/api/lists", new { name = "People" });
        await client.PostAsync<ContactDto>($"/api/lists/{list.Id}/contacts",
            new { data = new Dictionary<string, string> { ["Email"] = "bob@example.com" } });

        var added = await client.PostAsync<AddedDto>("/api/opt-outs",
            new { emails = "BOB@example.com, carol@example.com;\nnonsense", reason = "Asked by phone" });

        added.Added.Should().Be(2);
        added.Ignored.Should().Equal("nonsense");
        var optOuts = await client.GetAsync<List<OptOutDto>>("/api/opt-outs");
        optOuts.Select(o => o.Email).Should().BeEquivalentTo("bob@example.com", "carol@example.com");
        optOuts.Should().OnlyContain(o => o.Reason == "Asked by phone" && o.Source == "manual");

        var contacts = await client.GetAsync<ListContactsDto>($"/api/lists/{list.Id}/contacts");
        contacts.Contacts.Single().OptedOut.Should().BeTrue();

        (await client.PostAsync<AddedDto>("/api/opt-outs", new { emails = "bob@example.com" })).AlreadyOptedOut.Should().Be(1);
    }

    [Fact]
    public async Task Search_by_email_and_remove()
    {
        var client = api.CreateStaffClient();
        await client.PostAsync<AddedDto>("/api/opt-outs", new { emails = "dan@example.com eve@example.com" });

        var found = await client.GetAsync<List<OptOutDto>>("/api/opt-outs?email=DAN@example.com");
        found.Should().ContainSingle().Which.Email.Should().Be("dan@example.com");

        (await client.DeleteAsync($"/api/opt-outs/{found[0].Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync<List<OptOutDto>>("/api/opt-outs")).Select(o => o.Email).Should().Equal("eve@example.com");
    }

    [Fact]
    public async Task Opt_outs_are_per_workspace()
    {
        var shopA = api.CreateStaffClient();
        var shopB = api.CreateStaffClient();
        await shopA.PostAsync<AddedDto>("/api/opt-outs", new { emails = "x@example.com" });

        (await shopB.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().BeEmpty();
        (await shopB.GetAsync<List<OptOutDto>>("/api/opt-outs?email=x@example.com")).Should().BeEmpty();
    }
}
