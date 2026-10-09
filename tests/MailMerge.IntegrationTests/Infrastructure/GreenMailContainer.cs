using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace MailMerge.IntegrationTests.Infrastructure;

/// <summary>A real SMTP + IMAP server. User <c>shop</c>/<c>secret</c>; recipients get mailboxes automatically.</summary>
public sealed class GreenMailContainer : IAsyncLifetime
{
    public const string User = "shop";
    public const string Password = "secret";

    private readonly IContainer _container = new ContainerBuilder("greenmail/standalone:2.1.14")
        .WithEnvironment("GREENMAIL_OPTS",
            "-Dgreenmail.setup.test.smtp -Dgreenmail.setup.test.imap -Dgreenmail.hostname=0.0.0.0 " +
            $"-Dgreenmail.users={User}:{Password}@shop.test -Dgreenmail.verbose")
        .WithPortBinding(3025, true)
        .WithPortBinding(3143, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Starting GreenMail standalone"))
        .Build();

    public string Host => _container.Hostname;
    public int SmtpPort => _container.GetMappedPublicPort(3025);
    public int ImapPort => _container.GetMappedPublicPort(3143);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        // The log line appears just before the listeners bind.
        await Task.Delay(1500);
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}
