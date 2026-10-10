using System.Net;
using System.Text.RegularExpressions;
using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static MailMerge.IntegrationTests.Infrastructure.Delivery;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Privacy;

[Collection(ApiCollection.Name)]
public sealed partial class UnsubscribeTests(MailMergeApiFactory api)
{
    private async Task<(HttpClient Staff, string LinkPath)> SentWithFooter(string email = "annie@example.com", string? message = null)
    {
        var staff = api.CreateStaffClient("unsub-" + Guid.NewGuid().ToString("N")[..6]);
        var account = await CreateTestAccountAsync(staff);
        await CreateListAsync(staff, Person(email, "Annie"));
        var campaign = await CreateCampaignAsync(staff, "News", "Hello");
        await staff.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { unsubscribeFooter = true, unsubscribeMessage = message });
        await RunAndWaitAsync(staff, campaign.Id, "send");

        var text = api.Services.GetRequiredService<TestMailbox>().For(account.Id).Sent.Single().TextBody;
        text.Should().Contain(message ?? "Don't want these emails?");
        var url = Link().Match(text).Value;
        return (staff, new Uri(url).PathAndQuery);
    }

    private HttpClient Recipient() => api.CreateClientWith(c => c.DefaultRequestHeaders.Add(TestAuthHandler.NoAuthHeader, "1"));

    [Fact]
    public async Task Custom_message_keeps_a_working_unsubscribe_link_in_delivered_email()
    {
        var (staff, path) = await SentWithFooter(message: "You can stop receiving our updates below.");

        (await Recipient().PostAsync(path, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await staff.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().ContainSingle();
    }

    [Fact]
    public async Task Link_shows_a_confirmation_then_opts_the_person_out()
    {
        var (staff, path) = await SentWithFooter();
        var recipient = Recipient();

        var page = await recipient.GetStringAsync(path);
        page.Should().Contain("Unsubscribe?").And.Contain("a****@example.com").And.Contain("Unsub ", "the shop name comes from the workspace");
        (await staff.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().BeEmpty("viewing the page changes nothing");

        var confirmed = await recipient.PostAsync(path, null);
        confirmed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await confirmed.Content.ReadAsStringAsync()).Should().Contain("You're unsubscribed");

        var optOut = (await staff.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().ContainSingle().Subject;
        optOut.Email.Should().Be("annie@example.com");
        optOut.Source.Should().Be("link");
        (await recipient.GetStringAsync(path)).Should().Contain("already unsubscribed");
    }

    [Fact]
    public async Task One_click_post_from_a_mail_client_works_and_is_idempotent()
    {
        var (staff, path) = await SentWithFooter();
        var oneClick = new FormUrlEncodedContent([new("List-Unsubscribe", "One-Click")]);

        (await Recipient().PostAsync(path, oneClick)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Recipient().PostAsync(path, oneClick)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await staff.GetAsync<List<OptOutDto>>("/api/opt-outs")).Should().ContainSingle();
    }

    [Fact]
    public async Task Tampered_links_are_rejected()
    {
        var (_, path) = await SentWithFooter();

        var response = await Recipient().GetAsync(path[..^4] + "AAAA");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Link not recognised");
    }

    [GeneratedRegex(@"https://mailmerge\.test/u/[A-Za-z0-9_\-]+")]
    private static partial Regex Link();
}
