using System.Net;
using System.Net.Http.Json;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using static MailMerge.IntegrationTests.Infrastructure.Delivery;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Campaigns;

/// <summary>
/// The background worker end to end, in test mode. Ports test_create_drafts_then_send, test_rerun_only_new_people,
/// test_html_mode_escapes_values, test_crash_mid_send_is_never_resent and test_auth_problem_pauses_instead_of_failing.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class DeliveryTests(MailMergeApiFactory api)
{
    private TestMailbox.Folders Outbox(long accountId) => api.Services.GetRequiredService<TestMailbox>().For(accountId);

    private async Task<(HttpClient Client, AccountDto Account, ListDto List)> ShopWithContacts(params Dictionary<string, string>[] people)
    {
        var client = api.CreateStaffClient();
        var account = await CreateTestAccountAsync(client);
        var list = await CreateListAsync(client, people);
        return (client, account, list);
    }

    [Fact]
    public async Task Drafts_first_then_send_skipping_invalid_opted_out_and_do_not_email()
    {
        var (client, account, list) = await ShopWithContacts(
            Person("ann@example.com", "Ann", "Acme"), Person("bob@example.com", "Bob", "Beta"),
            Person("not-an-email", "Cy"), Person("eve@example.com", "Eve"), Person("fay@example.com", "Fay"));
        await client.PostAsync<object>("/api/opt-outs", new { emails = "bob@example.com" });
        var eve = (await client.GetAsync<ListContactsDto>($"/api/lists/{list.Id}/contacts")).Contacts.Single(c => c.Email == "eve@example.com");
        await client.PutAsync<ContactDto>($"/api/contacts/{eve.Id}", new { doNotEmail = true });
        var campaign = await CreateCampaignAsync(client, "Hi {{First Name|there}} at {{Company|home}}", "Dear {{First Name}}");

        var drafted = await RunAndWaitAsync(client, campaign.Id, "draft");

        drafted.Status.Should().Be("done");
        drafted.Counts.Should().Contain(new Dictionary<string, int> { ["drafted"] = 2, ["skipped"] = 3, ["total"] = 5 });
        Outbox(account.Id).Drafts.Values.Select(m => m.Subject).Should().BeEquivalentTo("Hi Ann at Acme", "Hi Fay at home");
        Outbox(account.Id).Sent.Should().BeEmpty();
        var messages = await MessagesAsync(client, campaign.Id);
        messages.Single(m => m.Email == "bob@example.com").Error.Should().Be("On the opt-out list");
        messages.Single(m => m.Email == "eve@example.com").Error.Should().Be("Marked do-not-email");
        messages.Single(m => m.Email == "not-an-email").Error.Should().Be("Invalid email address");

        var sent = await RunAndWaitAsync(client, campaign.Id, "send");

        sent.Counts["sent"].Should().Be(2);
        Outbox(account.Id).Drafts.Should().BeEmpty("the existing drafts were sent, not new ones made");
        Outbox(account.Id).Sent.Select(m => m.To.ToString()).Should().BeEquivalentTo("ann@example.com", "fay@example.com");
        var plan = await client.GetAsync<PlanResponseDto>($"/api/campaigns/{campaign.Id}/plan");
        plan.Plan.AlreadySent.Should().Be(2);
        plan.Plan.ToSend.Should().Be(0);
    }

    [Fact]
    public async Task Rerunning_only_emails_people_added_since()
    {
        var (client, account, list) = await ShopWithContacts(Person("ann@example.com", "Ann"));
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi {{First Name}}");
        await RunAndWaitAsync(client, campaign.Id, "send");

        await client.PostAsync<ContactDto>($"/api/lists/{list.Id}/contacts", new { data = Person("new@example.com", "New") });
        var again = await RunAndWaitAsync(client, campaign.Id, "send");

        again.Counts["sent"].Should().Be(2);
        Outbox(account.Id).Sent.Select(m => m.To.ToString()).Should().Equal("ann@example.com", "new@example.com");
    }

    [Fact]
    public async Task Html_mode_escapes_contact_values_in_the_sent_email()
    {
        var (client, account, _) = await ShopWithContacts(Person("fay@example.com", "Fay <b>"));
        var campaign = await CreateCampaignAsync(client, "Hello {{First Name}}", "<p>Hi {{First Name}}</p>", isHtml: true);

        await RunAndWaitAsync(client, campaign.Id, "send");

        var sent = Outbox(account.Id).Sent.Single();
        sent.HtmlBody.Should().Contain("<p>Hi Fay &lt;b&gt;</p>");
        sent.Subject.Should().Be("Hello Fay <b>", "subjects are plain text");
    }

    [Fact]
    public async Task Unsubscribe_footer_adds_a_working_link_and_headers()
    {
        var (client, account, _) = await ShopWithContacts(Person("ann@example.com", "Ann"));
        var campaign = await CreateCampaignAsync(client, "News", "Hello");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { unsubscribeFooter = true });

        await RunAndWaitAsync(client, campaign.Id, "send");

        var sent = Outbox(account.Id).Sent.Single();
        sent.TextBody.Should().Contain($"{MailMergeApiFactory.PublicBaseUrl}/u/");
        sent.Headers["List-Unsubscribe"].Should().StartWith($"<{MailMergeApiFactory.PublicBaseUrl}/u/");
    }

    [Fact]
    public async Task A_message_interrupted_mid_send_is_never_resent_automatically()
    {
        var (client, account, _) = await ShopWithContacts(Person("ann@example.com", "Ann"), Person("bob@example.com", "Bob"));
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi");
        await RunAndWaitAsync(client, campaign.Id, "draft");
        // Simulate the app dying during Bob's send call.
        await api.WithDbAsync(db => db.Messages.IgnoreQueryFilters()
            .Where(m => m.CampaignId == campaign.Id && m.Email == "bob@example.com")
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MessageStatus.Sending)));

        await api.Services.GetRequiredService<CampaignWorker>().RecoverAsync(CancellationToken.None);
        var after = await RunAndWaitAsync(client, campaign.Id, "send");

        var bob = (await MessagesAsync(client, campaign.Id)).Single(m => m.Email == "bob@example.com");
        bob.Status.Should().Be("uncertain");
        bob.Error.Should().Be(CampaignWorker.InterruptedSend);
        after.Counts["sent"].Should().Be(1);
        Outbox(account.Id).Sent.Select(m => m.To.ToString()).Should().Equal("ann@example.com");

        (await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/retry-failed", new { })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MessagesAsync(client, campaign.Id)).Single(m => m.Email == "bob@example.com").Status
            .Should().Be("uncertain", "retry-failed never touches messages that might have been sent");

        var marked = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/messages/{bob.Id}/mark", new { status = "sent" }, TestData.Json);
        marked.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await MessagesAsync(client, campaign.Id)).Single(m => m.Email == "bob@example.com").Status.Should().Be("sent");
    }

    [Fact]
    public async Task Connection_lost_during_send_is_uncertain_not_failed()
    {
        var (client, account, _) = await ShopWithContacts(Person("ann@timeout.invalid"), Person("bob@example.com"));
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi");

        var done = await RunAndWaitAsync(client, campaign.Id, "send");

        done.Status.Should().Be("done");
        var ann = (await MessagesAsync(client, campaign.Id)).Single(m => m.Email == "ann@timeout.invalid");
        ann.Status.Should().Be("uncertain");
        ann.Error.Should().Contain("Check Sent Items");
        Outbox(account.Id).Sent.Select(m => m.To.ToString()).Should().Equal("bob@example.com");
    }

    [Fact]
    public async Task Account_problems_pause_instead_of_failing_every_row()
    {
        var client = api.CreateStaffClient();
        await client.PostAsync<AccountDto>("/api/accounts", new { name = "Outlook", kind = "microsoft" });
        await CreateListAsync(client, Person("ann@example.com"), Person("bob@example.com"));
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi");

        var paused = await RunAndWaitAsync(client, campaign.Id, "send");

        paused.Status.Should().Be("paused");
        paused.Note.Should().Contain("Microsoft sign-in isn't set up");
        (await MessagesAsync(client, campaign.Id)).Should().OnlyContain(m => m.Status == "pending");
    }

    [Fact]
    public async Task Five_failures_in_a_row_pause_the_campaign()
    {
        var (client, _, _) = await ShopWithContacts(Enumerable.Range(1, 7)
            .Select(i => Person($"p{i}@bounce.invalid")).ToArray());
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi");

        var paused = await RunAndWaitAsync(client, campaign.Id, "draft");

        paused.Status.Should().Be("paused");
        paused.Note.Should().StartWith("Paused after 5 failures in a row").And.Contain("rejected");
        paused.Counts["failed"].Should().Be(5);
        paused.Counts["pending"].Should().Be(2);

        var retried = await client.PostAsync<RetryDto>($"/api/campaigns/{campaign.Id}/retry-failed", new { });
        retried.Requeued.Should().Be(5);
    }

    [Fact]
    public async Task Run_checks_prerequisites_with_readable_messages()
    {
        var client = api.CreateStaffClient();
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "x" });

        async Task<string> Run(string mode = "send") =>
            await (await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/run", new { mode })).ProblemDetailAsync();

        (await Run()).Should().Be("Choose a sending account first");
        var smtp = await client.PostAsync<AccountDto>("/api/accounts",
            new { kind = "smtp", smtp = new { host = "localhost", port = 1, security = "none" } });
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { accountId = smtp.Id });
        (await Run()).Should().Be("Choose who to send to first");
        var list = await CreateListAsync(client);
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { listId = list.Id });
        (await Run()).Should().Be("Add a subject first");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { subject = "S" });
        (await Run()).Should().Be("Write the message first");
        await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { body = "B" });
        (await Run("draft")).Should().Contain("can't save drafts");
    }

    [Fact]
    public async Task Two_shops_send_at_the_same_time()
    {
        var (shopA, accountA, _) = await ShopWithContacts(Person("a1@example.com"), Person("a2@example.com"), Person("a3@example.com"));
        var (shopB, accountB, _) = await ShopWithContacts(Person("b1@example.com"), Person("b2@example.com"), Person("b3@example.com"));
        var campaignA = await CreateCampaignAsync(shopA, "A", "a");
        var campaignB = await CreateCampaignAsync(shopB, "B", "b");

        await shopA.PostAsJsonAsync($"/api/campaigns/{campaignA.Id}/run", new { mode = "send" });
        await shopB.PostAsJsonAsync($"/api/campaigns/{campaignB.Id}/run", new { mode = "send" });
        await WaitAsync(shopA, campaignA.Id);
        await WaitAsync(shopB, campaignB.Id);

        Outbox(accountA.Id).Sent.Should().HaveCount(3);
        Outbox(accountB.Id).Sent.Should().HaveCount(3);
        var times = await api.WithDbAsync(db => db.Messages.IgnoreQueryFilters()
            .Where(m => m.CampaignId == campaignA.Id || m.CampaignId == campaignB.Id)
            .OrderBy(m => m.Updated).Select(m => m.CampaignId).ToListAsync());
        times.Take(3).Should().Contain(campaignA.Id).And.Contain(campaignB.Id, "each account is paced separately, so they interleave");
    }

    [Fact]
    public async Task Log_csv_lists_every_recipient()
    {
        var (client, _, _) = await ShopWithContacts(Person("ann@example.com"), Person("=cmd@example.com"));
        var campaign = await CreateCampaignAsync(client, "Hello", "Hi");
        await RunAndWaitAsync(client, campaign.Id, "send");

        var csv = await client.GetStringAsync($"/api/campaigns/{campaign.Id}/log.csv");

        csv.Should().Contain("Email,Status,Note,Updated (UTC)").And.Contain("ann@example.com,sent,");
        csv.Should().Contain("'=cmd@example.com", "values that look like formulas are defused for Excel");
    }

    private sealed record RetryDto(int Requeued);
}
