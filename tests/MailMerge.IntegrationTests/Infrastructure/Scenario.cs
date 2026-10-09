using System.Net.Http.Json;

namespace MailMerge.IntegrationTests.Infrastructure;

/// <summary>Builds the usual starting point: a workspace with a contact list (and optionally a campaign).</summary>
public static class Scenario
{
    public static async Task<ListDto> CreateListAsync(HttpClient client, params Dictionary<string, string>[] contacts)
    {
        var list = await client.PostAsync<ListDto>("/api/lists", new { name = "Clients" });
        foreach (var contact in contacts)
        {
            await client.PostAsync<ContactDto>($"/api/lists/{list.Id}/contacts", new { data = contact });
        }

        return list;
    }

    public static Dictionary<string, string> Person(string email, string firstName = "", string company = "", string suburb = "") =>
        new() { ["Email"] = email, ["First Name"] = firstName, ["Company"] = company, ["Suburb"] = suburb };

    public static async Task<CampaignDto> CreateCampaignAsync(HttpClient client, string subject, string body, bool isHtml = false)
    {
        var campaign = await client.PostAsync<CampaignDto>("/api/campaigns", new { name = "Spring" });
        return await client.PutAsync<CampaignDto>($"/api/campaigns/{campaign.Id}", new { subject, body, isHtml });
    }
}

public static class Delivery
{
    public sealed record AccountDto(long Id, string Name);

    public sealed record MessageDto(long Id, string Email, string Status, string? Error);

    public static Task<AccountDto> CreateTestAccountAsync(HttpClient client, int perMinute = 120) =>
        client.PostAsync<AccountDto>("/api/accounts",
            new { name = "Test", kind = "test", fromEmail = "shop@work.test", perMinute });

    public static async Task<CampaignDto> RunAndWaitAsync(HttpClient client, long campaignId, string mode)
    {
        var started = await client.PostAsJsonAsync($"/api/campaigns/{campaignId}/run", new { mode });
        if (!started.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(await started.Content.ReadAsStringAsync());
        }

        return await WaitAsync(client, campaignId);
    }

    public static async Task<CampaignDto> WaitAsync(HttpClient client, long campaignId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var campaign = await client.GetAsync<CampaignDto>($"/api/campaigns/{campaignId}");
            if (campaign.Status is "done" or "paused")
            {
                return campaign;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"Campaign {campaignId} didn't finish");
    }

    public static Task<List<MessageDto>> MessagesAsync(HttpClient client, long campaignId) =>
        client.GetAsync<List<MessageDto>>($"/api/campaigns/{campaignId}/messages");
}
