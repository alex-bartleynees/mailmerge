using System.Net;
using System.Net.Http.Json;
using MailMerge.IntegrationTests.Infrastructure;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Campaigns;

/// <summary>Ports test_preview_and_unknown_fields, test_attachment_upload and the preview half of
/// test_html_mode_escapes_values.</summary>
[Collection(ApiCollection.Name)]
public sealed class CampaignEditingTests(MailMergeApiFactory api)
{
    [Fact]
    public async Task New_campaign_defaults_to_the_newest_list()
    {
        var client = api.CreateStaffClient();
        await CreateListAsync(client);
        var newest = await CreateListAsync(client);

        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "" });

        campaign.Name.Should().Be("New email");
        campaign.ListId.Should().Be(newest.Id);
        campaign.Status.Should().Be("idle");
        campaign.Mode.Should().Be("draft");
    }

    [Fact]
    public async Task Preview_merges_fields_and_reports_unknown_and_empty_ones()
    {
        var client = api.CreateStaffClient();
        await CreateListAsync(client, Person("ann@example.com", "Ann", "Acme", "Hutt"), Person("bob@example.com", "", "Beta"));
        var campaign = await CreateCampaignAsync(client,
            "Hi {{First Name|there}} at {{Company}}",
            "Dear {{ First Name }},\nSuburb: {{Suburb|unknown}}\n{{Missing}}\n");

        var ann = await client.GetAsync<PreviewDto>($"/api/campaigns/{campaign.Id}/preview");

        ann.Subject.Should().Be("Hi Ann at Acme");
        ann.Body.Should().Contain("Suburb: Hutt");
        ann.UnknownFields.Should().Equal("Missing");
        ann.Total.Should().Be(2);
        ann.PreviousId.Should().BeNull();
        ann.NextId.Should().NotBeNull();

        var bob = await client.GetAsync<PreviewDto>($"/api/campaigns/{campaign.Id}/preview?contactId={ann.NextId}");
        bob.To.Should().Be("bob@example.com");
        bob.Subject.Should().Be("Hi there at Beta");
        bob.EmptyFields.Should().Equal("First Name", "Suburb");
        bob.PreviousId.Should().Be(ann.ContactId);
    }

    [Fact]
    public async Task Html_preview_escapes_contact_values()
    {
        var client = api.CreateStaffClient();
        await CreateListAsync(client, Person("fay@example.com", "Fay <b>"));
        var campaign = await CreateCampaignAsync(client, "Hello", "<p>Hi {{First Name}}</p>", isHtml: true);

        var preview = await client.GetAsync<PreviewDto>($"/api/campaigns/{campaign.Id}/preview");

        preview.Body.Should().Be("<p>Hi Fay &lt;b&gt;</p>");
    }

    [Fact]
    public async Task Unsubscribe_footer_shows_in_the_preview()
    {
        var client = api.CreateStaffClient();
        await CreateListAsync(client, Person("ann@example.com", "Ann"));
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { unsubscribeFooter = true });

        var preview = await client.GetAsync<PreviewDto>($"/api/campaigns/{campaign.Id}/preview");

        preview.Body.Should().StartWith("Hi").And.Contain("Unsubscribe here: https://mailmerge.test/");
    }

    [Fact]
    public async Task Attachments_upload_list_and_remove()
    {
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "With file" });

        var upload = await client.PostAsync($"/api/campaigns/{campaign.Id}/attachments",
            TestData.File("price list.txt", "hello attachment"u8.ToArray()));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);

        var details = await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}");
        var attachment = details.Attachments.Should().ContainSingle().Subject;
        attachment.FileName.Should().Be("price list.txt");
        attachment.ContentType.Should().Be("text/plain");
        attachment.Size.Should().Be(16);

        (await client.DeleteAsync($"/api/campaigns/{campaign.Id}/attachments/{attachment.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync<CampaignDto>($"/api/campaigns/{campaign.Id}")).Attachments.Should().BeEmpty();
    }

    [Fact]
    public async Task Copy_duplicates_content_and_attachments_without_history()
    {
        var client = api.CreateStaffClient();
        var campaign = await CreateCampaignAsync(client, "Subject", "Body");
        await client.PostAsync($"/api/campaigns/{campaign.Id}/attachments", TestData.File("a.pdf", [1, 2, 3]));

        var copy = await (await client.PostAsync($"/api/campaigns/{campaign.Id}/copy", null)).ReadAsync<CampaignDto>();

        copy.Id.Should().NotBe(campaign.Id);
        copy.Name.Should().Be("Spring (copy)");
        copy.Subject.Should().Be("Subject");
        copy.Attachments.Should().ContainSingle().Which.FileName.Should().Be("a.pdf");
        copy.Counts["total"].Should().Be(0);
    }

    [Fact]
    public async Task Plan_counts_who_would_be_skipped()
    {
        var client = api.CreateStaffClient();
        var list = await CreateListAsync(client,
            Person("ann@example.com"), Person("not-an-email"), Person("opted@example.com"), Person("dne@example.com"));
        await client.PostAsync<object>("/api/opt-outs", new { emails = "opted@example.com" });
        var contacts = await client.GetAsync<ListContactsDto>($"/api/lists/{list.Id}/contacts");
        await client.PutAsync<ContactDto>($"/api/contacts/{contacts.Contacts.Single(c => c.Email == "dne@example.com").Id}",
            new { doNotEmail = true });
        var campaign = await CreateCampaignAsync(client, "S", "B");

        var plan = await client.GetAsync<PlanResponseDto>($"/api/campaigns/{campaign.Id}/plan");

        plan.Plan.Should().BeEquivalentTo(new PlanDto(4, 3, 0, 1, 1, 0, 0, 0));
        plan.Account.Should().BeNull();
    }

    [Fact]
    public async Task Pointing_at_another_workspaces_list_is_rejected()
    {
        var other = await CreateListAsync(api.CreateStaffClient());
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "x" });

        var response = await client.PutAsJsonAsync($"/api/campaigns/{campaign.Id}", new { listId = other.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.ProblemDetailAsync()).Should().Be("That contact list no longer exists");
    }

    [Fact]
    public async Task Another_workspace_cannot_see_the_campaign()
    {
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "Private" });
        var other = api.CreateStaffClient();

        (await other.GetAsync<List<CampaignDto>>("/api/campaigns")).Should().BeEmpty();
        (await other.GetAsync($"/api/campaigns/{campaign.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/campaigns/{campaign.Id}/preview")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/campaigns/{campaign.Id}/copy", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/campaigns/{campaign.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
