using System.Net;
using System.Net.Http.Json;
using System.Text;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static MailMerge.IntegrationTests.Infrastructure.Scenario;

namespace MailMerge.IntegrationTests.Accounts;

[Collection(ApiCollection.Name)]
public sealed class AccountTests(MailMergeApiFactory api)
{
    [Fact]
    public async Task Passwords_are_never_returned_kept_when_blank_and_encrypted_at_rest()
    {
        var client = api.CreateStaffClient();

        var created = await client.PostAsync<AccountDto>("/api/accounts", new
        {
            name = "S", kind = "smtp", fromEmail = "a@b.test",
            smtp = new { host = "h", port = 587, security = "starttls", username = "u" },
            password = "secret-pw",
        });
        var raw = await client.GetStringAsync("/api/accounts");
        raw.Should().NotContain("secret-pw");
        created.HasPassword.Should().BeTrue();

        var updated = await client.PutAsync<AccountDto>($"/api/accounts/{created.Id}", new
        {
            name = "S2", smtp = new { host = "h2", port = 587, security = "starttls", username = "u" }, password = "",
        });
        updated.HasPassword.Should().BeTrue("a blank password means keep the saved one");
        updated.Smtp!.Host.Should().Be("h2");

        var stored = await api.WithDbAsync(db => db.SendingAccounts.IgnoreQueryFilters().SingleAsync(a => a.Id == created.Id));
        Encoding.UTF8.GetString(stored.ProtectedSecrets!).Should().NotContain("secret-pw");
    }

    [Fact]
    public async Task Invalid_settings_are_rejected_with_a_readable_message()
    {
        var client = api.CreateStaffClient();

        var noHost = await client.PostAsJsonAsync("/api/accounts", new { kind = "smtp", smtp = new { host = "" } }, TestData.Json);
        var badFrom = await client.PostAsJsonAsync("/api/accounts", new { kind = "test", fromEmail = "nope" }, TestData.Json);
        var tooFast = await client.PostAsJsonAsync("/api/accounts", new { kind = "test", perMinute = 1000 }, TestData.Json);

        (await noHost.ProblemDetailAsync()).Should().Be("Enter the mail server (SMTP) address");
        (await badFrom.ProblemDetailAsync()).Should().Contain("doesn't look like an email address");
        tooFast.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Test_account_check_and_test_send_with_first_contacts_details()
    {
        var client = api.CreateStaffClient();
        var account = await client.PostAsync<AccountDto>("/api/accounts", new { name = "Test", kind = "test", fromEmail = "shop@work.test" });
        (await client.PostAsync<CheckDto>($"/api/accounts/{account.Id}/check", new { })).Ok.Should().BeTrue();
        await CreateListAsync(client, Person("ann@example.com", "Ann"));
        var campaign = await CreateCampaignAsync(client, "Hi {{First Name}}", "Body for {{First Name}}");
        campaign.AccountId.Should().Be(account.Id, "new emails default to the first account");
        await client.PostAsync($"/api/campaigns/{campaign.Id}/attachments", TestData.File("a.txt", "x"u8.ToArray()));

        var result = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/test", new { to = "me@work.test" });

        result.StatusCode.Should().Be(HttpStatusCode.OK);
        var outbox = await client.GetAsync<OutboxDto>($"/api/accounts/{account.Id}/test-outbox");
        var sent = outbox.Sent.Should().ContainSingle().Subject;
        sent.To.Should().Be("me@work.test");
        sent.Subject.Should().Be("[TEST] Hi Ann");
        sent.Text.Should().Contain("Body for Ann");
        sent.Attachments.Should().Equal("a.txt");
    }

    [Fact]
    public async Task Test_send_requires_a_valid_address_and_an_account()
    {
        var client = api.CreateStaffClient();
        var campaign = await CreateCampaignAsync(client, "S", "B");

        var badTo = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/test", new { to = "me" });
        var noAccount = await client.PostAsJsonAsync($"/api/campaigns/{campaign.Id}/test", new { to = "me@work.test" });

        (await badTo.ProblemDetailAsync()).Should().Be("Enter your own email address to send the test to");
        (await noAccount.ProblemDetailAsync()).Should().Be("Choose a sending account first");
    }

    [Fact]
    public async Task Microsoft_sign_in_needs_server_configuration()
    {
        var client = api.CreateStaffClient();
        var account = await client.PostAsync<AccountDto>("/api/accounts", new { name = "Outlook", kind = "microsoft" });

        var response = await client.PostAsync($"/api/accounts/{account.Id}/microsoft/sign-in", null);

        (await response.ProblemDetailAsync()).Should().Contain("isn't set up on this server");
        account.Connected.Should().BeFalse();
        var check = await client.PostAsync<CheckDto>($"/api/accounts/{account.Id}/check", new { });
        check.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task Callback_with_an_unknown_state_redirects_back_with_an_error()
    {
        var noRedirects = api.CreateClient(new() { AllowAutoRedirect = false });
        noRedirects.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, "/mailmerge/x");

        var response = await noRedirects.GetAsync("/api/accounts/microsoft/callback?code=abc&state=nope");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!.ToString();
        location.Should().StartWith("/?microsoft=error").And.EndWith("#accounts");
        Uri.UnescapeDataString(location).Should().Contain("took too long or was already used");
    }

    [Fact]
    public async Task Deleting_an_account_another_workspace_owns_is_not_found()
    {
        var owner = api.CreateStaffClient();
        var account = await owner.PostAsync<AccountDto>("/api/accounts", new { kind = "test" });

        (await api.CreateStaffClient().DeleteAsync($"/api/accounts/{account.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await owner.DeleteAsync($"/api/accounts/{account.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private sealed record SmtpDto(string Host, int Port);

    private sealed record AccountDto(long Id, string Name, string Kind, string FromEmail, SmtpDto? Smtp, bool HasPassword, bool Connected);

    private sealed record CheckDto(bool Ok, string Message);

    private sealed record OutboxItem(string To, string Subject, string Text, List<string> Attachments);

    private sealed record OutboxDto(List<OutboxItem> Drafts, List<OutboxItem> Sent);
}
