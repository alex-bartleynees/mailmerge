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
