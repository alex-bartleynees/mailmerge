using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Privacy.ErasePerson;

/// <summary>
/// "Delete this person everywhere", in one transaction: every contact row and per-recipient row with the address,
/// any spreadsheets awaiting import, and the plain address on their opt-out (the hash stays unless told otherwise).
/// The privacy log records counts only.
/// </summary>
public sealed class ErasePersonHandler(
    MailMergeDbContext db,
    OptOutRegistry registry,
    CurrentUser user,
    TimeProvider clock) : IHandler
{
    public async Task<Result<ErasePersonResponse>> Handle(ErasePersonRequest request, CancellationToken ct)
    {
        var email = EmailAddress.Normalize(request.Email);
        var hash = (await registry.HasherAsync(user.WorkspaceId, ct))(email);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Lock this person's rows: the worker's conditional claim on any of them now waits for us, then finds the
        // row gone and moves on, so nothing is sent to someone mid-erasure.
        var statuses = await db.Messages
            .FromSql($"SELECT * FROM messages WHERE workspace_id = {user.WorkspaceId} AND email = {email} FOR UPDATE")
            .Select(m => m.Status)
            .ToListAsync(ct);
        if (statuses.Any(s => s is MessageStatus.Drafting or MessageStatus.Sending))
        {
            return PrivacyErrors.SendingNow;
        }

        var drafts = (await PersonRecords.FindAsync(db, hash, email, ct)).Campaigns
            .Where(c => c.DraftInMailbox)
            .Select(c => new ErasePersonResponse.Draft(c.CampaignName, c.Account))
            .ToList();

        var contacts = await db.Contacts.Where(c => c.Email == email).ExecuteDeleteAsync(ct);
        var messages = await db.Messages.Where(m => m.Email == email).ExecuteDeleteAsync(ct);
        var imports = await db.ImportUploads.ExecuteDeleteAsync(ct);

        var now = clock.GetUtcNow();
        var optOut = await db.OptOuts.SingleOrDefaultAsync(o => o.EmailHash == hash, ct);
        if (request.KeepOptedOut)
        {
            if (optOut is null)
            {
                db.OptOuts.Add(new OptOut
                {
                    WorkspaceId = user.WorkspaceId,
                    EmailHash = hash,
                    Added = now,
                    Reason = "Asked to be removed",
                    Source = OptOutSource.Erasure,
                });
            }
            else
            {
                optOut.Email = null;
                optOut.Source = OptOutSource.Erasure;
            }
        }
        else if (optOut is not null)
        {
            db.OptOuts.Remove(optOut);
        }

        db.PrivacyLog.Add(new PrivacyLogEntry
        {
            WorkspaceId = user.WorkspaceId,
            Action = "erase",
            PerformedBy = user.DisplayIdentity,
            PerformedAt = now,
            Counts = new() { ["contacts"] = contacts, ["messages"] = messages, ["imports"] = imports },
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new ErasePersonResponse(contacts, messages, imports, request.KeepOptedOut, drafts);
    }
}
