using MailMerge.Api.Shared.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Testcontainers.PostgreSql;

namespace MailMerge.IntegrationTests.Infrastructure;

/// <summary>
/// The real API against a real Postgres (Testcontainers). Only Keycloak is replaced (<see cref="TestAuthHandler"/>).
/// One instance is shared by the whole test run; tests isolate themselves by using their own workspace.
/// </summary>
public sealed class MailMergeApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public const string PublicBaseUrl = "https://mailmerge.test";

    /// <summary>Settings a test class can extend before the host is first built.</summary>
    public Dictionary<string, string?> Settings { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Build the host now so migrations run once, before any test.
        _ = Services;
    }

    /// <summary>Simulates a real database outage; only use with an isolated factory.</summary>
    public Task StopDatabaseAsync() => _postgres.StopAsync();

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MailMerge"] = _postgres.GetConnectionString(),
            ["App:PublicBaseUrl"] = PublicBaseUrl,
            ["Auth:Authority"] = "https://keycloak.test/realms/test",
            ["Auth:RequireHttpsMetadata"] = "false",
        }.Concat(Settings)));

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(o =>
                {
                    o.DefaultScheme = TestAuthHandler.SchemeName;
                    o.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                    o.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        });
    }

    /// <summary>A client acting as a staff member of <paramref name="workspace"/> (a fresh one if omitted).</summary>
    public HttpClient CreateStaffClient(string? workspace = null, string subject = "staff-1")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader,
            $"/mailmerge/{workspace ?? NewWorkspace()}");
        return client;
    }

    public HttpClient CreateClientWith(Action<HttpClient> configure)
    {
        var client = CreateClient();
        configure(client);
        return client;
    }

    public static string NewWorkspace() => "shop-" + Guid.NewGuid().ToString("N")[..10];

    /// <summary>Runs <paramref name="action"/> with a DbContext that sees every workspace.</summary>
    public async Task<T> WithDbAsync<T>(Func<MailMergeDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<MailMergeDbContext>());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<MailMergeApiFactory>
{
    public const string Name = "Api";
}
