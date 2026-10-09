using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Contacts.Delete;

public sealed class DeleteContactHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long contactId, CancellationToken ct)
    {
        var deleted = await db.Contacts.Where(c => c.Id == contactId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Result.Failure(ContactErrors.NotFound) : Result.Success();
    }
}
