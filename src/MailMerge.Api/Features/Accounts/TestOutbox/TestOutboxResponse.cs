namespace MailMerge.Api.Features.Accounts.TestOutbox;

public sealed record TestOutboxResponse(IReadOnlyList<TestOutboxResponse.Item> Drafts, IReadOnlyList<TestOutboxResponse.Item> Sent)
{
    public sealed record Item(string To, string Subject, DateTimeOffset Date, string Text, IReadOnlyList<string> Attachments);
}
