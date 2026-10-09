using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using MimeKit;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.TestOutbox;

/// <summary>What a test account "sent", so staff can check the merge before using a real mailbox.</summary>
public sealed class GetTestOutboxHandler(MailMergeDbContext db, TestMailbox mailbox) : IHandler
{
    public async Task<Result<TestOutboxResponse>> Handle(long accountId, CancellationToken ct)
    {
        var kind = await db.SendingAccounts.Where(a => a.Id == accountId).Select(a => (AccountKind?)a.Kind).SingleOrDefaultAsync(ct);
        if (kind is null)
        {
            return AccountErrors.NotFound;
        }

        if (kind != AccountKind.Test)
        {
            return AccountErrors.NotTest;
        }

        var folders = mailbox.For(accountId);
        return new TestOutboxResponse(
            folders.Drafts.Values.Select(ToItem).OrderBy(i => i.Date).ToList(),
            folders.Sent.Select(ToItem).ToList());
    }

    private static TestOutboxResponse.Item ToItem(MimeMessage m) =>
        new(m.To.ToString(), m.Subject ?? "", m.Date, m.TextBody ?? "", m.Attachments.Select(a => a.ContentDisposition?.FileName ?? "").ToList());
}
