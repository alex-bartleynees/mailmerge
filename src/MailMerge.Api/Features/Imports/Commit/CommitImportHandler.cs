using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Features.Lists;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Imports.Commit;

public sealed class CommitImportHandler(MailMergeDbContext db, CurrentUser user, TimeProvider clock) : IHandler
{
    public async Task<Result<CommitImportResponse>> Handle(Guid token, CommitImportRequest request, CancellationToken ct)
    {
        var upload = await db.ImportUploads.SingleOrDefaultAsync(u => u.Token == token, ct);
        if (upload is null)
        {
            return ImportErrors.Expired;
        }

        SheetTable table;
        try
        {
            table = SpreadsheetReader.Read(upload.Content, upload.FileName, request.Sheet);
        }
        catch (SpreadsheetException e)
        {
            return ImportErrors.Unreadable(e.Message);
        }

        var emailIndex = table.Headers.ToList().IndexOf(request.EmailColumn);
        if (emailIndex < 0)
        {
            return ImportErrors.EmailColumnRequired;
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        ContactList? list;
        if (request.ListId is { } listId)
        {
            list = await db.ContactLists.SingleOrDefaultAsync(l => l.Id == listId, ct);
            if (list is null)
            {
                return ListErrors.NotFound;
            }
        }
        else
        {
            list = new ContactList
            {
                WorkspaceId = user.WorkspaceId,
                Name = string.IsNullOrWhiteSpace(request.NewListName) ? "Imported list" : request.NewListName.Trim(),
                Created = clock.GetUtcNow(),
            };
            db.ContactLists.Add(list);
            await db.SaveChangesAsync(ct);
        }

        var (columns, mapping) = MapColumns(list.Columns, table.Headers, request.EmailColumn);
        list.Columns = columns;

        var existing = await db.Contacts.Where(c => c.ListId == list.Id).ToDictionaryAsync(c => c.Email, ct);
        int added = 0, updated = 0, duplicates = 0, invalid = 0, noEmail = 0;
        foreach (var row in table.Rows)
        {
            var email = EmailAddress.Normalize(row[emailIndex]);
            if (email.Length == 0)
            {
                noEmail++;
                continue;
            }

            var record = new Dictionary<string, string>();
            for (var i = 0; i < table.Headers.Count; i++)
            {
                record[mapping[i]] = row[i].Trim();
            }

            record[ContactList.EmailColumn] = email;

            if (existing.TryGetValue(email, out var contact))
            {
                if (request.UpdateExisting)
                {
                    // Blank cells in the file never wipe out data already in the list.
                    var merged = new Dictionary<string, string>(contact.Data);
                    foreach (var (k, v) in record.Where(kv => kv.Value.Length > 0))
                    {
                        merged[k] = v;
                    }

                    contact.Data = merged;
                    updated++;
                }
                else
                {
                    duplicates++;
                }

                continue;
            }

            contact = new Contact { WorkspaceId = user.WorkspaceId, ListId = list.Id, Email = email, Data = record };
            db.Contacts.Add(contact);
            existing[email] = contact;
            added++;
            if (!EmailAddress.IsValid(email))
            {
                invalid++;
            }
        }

        db.ImportUploads.Remove(upload);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new CommitImportResponse(list.Id, added, updated, duplicates, invalid, noEmail);
    }

    /// <summary>
    /// Maps each file header to a list column: the chosen email column becomes "Email", other headers match
    /// existing columns case-insensitively or are added. A second "email"-named header can't overwrite the
    /// address, so it becomes "&lt;header&gt; (2)". Merge-field characters are stripped from new names.
    /// </summary>
    internal static (List<string> Columns, string[] Mapping) MapColumns(
        IReadOnlyList<string> listColumns, IReadOnlyList<string> headers, string emailColumn)
    {
        var columns = listColumns.ToList();
        var byLower = columns.ToDictionary(c => c.ToLowerInvariant(), c => c);
        var mapping = new string[headers.Count];
        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            string target;
            if (header == emailColumn)
            {
                target = ContactList.EmailColumn;
            }
            else
            {
                var clean = new string(header.Where(ch => ch is not ('{' or '}' or '|')).ToArray()).Trim();
                if (clean.Length == 0)
                {
                    clean = $"Column {i + 1}";
                }

                target = byLower.GetValueOrDefault(clean.ToLowerInvariant(), clean);
                if (target == ContactList.EmailColumn)
                {
                    target = $"{clean} (2)";
                }
            }

            mapping[i] = target;
            if (byLower.TryAdd(target.ToLowerInvariant(), target))
            {
                columns.Add(target);
            }
        }

        return (columns, mapping);
    }
}
