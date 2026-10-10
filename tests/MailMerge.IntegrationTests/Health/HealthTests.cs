using System.Net;
using MailMerge.IntegrationTests.Infrastructure;

namespace MailMerge.IntegrationTests.Health;

// A separate fixture keeps the database outage isolated from the other integration tests.
public sealed class HealthTests(MailMergeApiFactory api) : IClassFixture<MailMergeApiFactory>
{
    [Fact]
    public async Task Database_outage_fails_readiness_but_preserves_liveness()
    {
        using var client = api.CreateClient();

        var healthy = await client.GetAsync("/health");
        healthy.StatusCode.Should().Be(HttpStatusCode.OK);
        (await healthy.Content.ReadAsStringAsync()).Should().Be("Healthy");

        await api.StopDatabaseAsync();

        var unavailable = await client.GetAsync("/health");
        unavailable.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await unavailable.Content.ReadAsStringAsync()).Should().Be("Unhealthy");

        var live = await client.GetAsync("/liveness");
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        (await live.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}
