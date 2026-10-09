using System.Collections.Concurrent;
using MimeKit;

namespace MailMerge.Api.Features.Accounts.Sending;

/// <summary>Test-mode outbox, held in memory per account. Lets staff try the whole flow without emailing anyone.</summary>
public sealed class TestMailbox
{
    private const int MaxPerFolder = 1000;

    private readonly ConcurrentDictionary<long, Folders> _accounts = new();

    public sealed class Folders
    {
        public ConcurrentDictionary<string, MimeMessage> Drafts { get; } = new();
        public ConcurrentQueue<MimeMessage> Sent { get; } = new();
    }

    public Folders For(long accountId) => _accounts.GetOrAdd(accountId, _ => new Folders());

    internal void Trim(Folders folders)
    {
        while (folders.Sent.Count > MaxPerFolder && folders.Sent.TryDequeue(out _))
        {
        }
    }
}

public sealed class TestMailSender(SendingAccount account, TestMailbox mailbox, TimeProvider clock) : IMailSender
{
    public bool SupportsDrafts => true;

    private TestMailbox.Folders Folders => mailbox.For(account.Id);

    public Task<string> CreateDraftAsync(OutgoingMessage message, CancellationToken ct)
    {
        var mime = Build(message);
        var id = Guid.NewGuid().ToString("N");
        Folders.Drafts[id] = mime;
        return Task.FromResult(id);
    }

    public Task SendDraftAsync(string draftId, CancellationToken ct)
    {
        if (!Folders.Drafts.TryRemove(draftId, out var mime))
        {
            throw new MessageRejectedException("Test draft not found.");
        }

        Folders.Sent.Enqueue(mime);
        mailbox.Trim(Folders);
        return Task.CompletedTask;
    }

    public Task SendNowAsync(OutgoingMessage message, CancellationToken ct)
    {
        Folders.Sent.Enqueue(Build(message));
        mailbox.Trim(Folders);
        return Task.CompletedTask;
    }

    public Task<string> CheckAsync(CancellationToken ct) =>
        Task.FromResult("Test mode: nothing is really sent. Messages are kept in the app's test outbox.");

    private MimeMessage Build(OutgoingMessage message) =>
        MimeBuilder.Build(message, account.FromEmail.Length > 0 ? account.FromEmail : "test@example.com",
            account.FromName, clock.GetUtcNow());
}
