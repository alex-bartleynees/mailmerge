using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.OptOuts.Remove;

/// <summary>Lets someone back on (e.g. they asked to rejoin). <paramref name="id"/> is the entry's hash.</summary>
public sealed class RemoveOptOutHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result> Handle(string id, CancellationToken ct)
    {
        var deleted = await db.OptOuts.Where(o => o.EmailHash == id).ExecuteDeleteAsync(ct);
        return deleted == 0 ? Result.Failure(OptOutErrors.NotFound) : Result.Success();
    }
}
