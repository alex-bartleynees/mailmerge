using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Lists.Delete;

/// <summary>Deletes the list and its contacts. Campaigns that used it keep their history but lose the list.</summary>
public sealed class DeleteListHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(long listId, CancellationToken ct)
    {
        var deleted = await db.ContactLists.Where(l => l.Id == listId).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Result.Failure(ListErrors.NotFound) : Result.Success();
    }
}
