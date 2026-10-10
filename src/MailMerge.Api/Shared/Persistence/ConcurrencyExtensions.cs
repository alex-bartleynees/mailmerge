using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Shared.Persistence;

public static class ConcurrencyExtensions
{
    /// <summary>
    /// Saves, turning an optimistic-concurrency conflict (the row's xmin changed since it was loaded, typically because
    /// the delivery worker updated it) into <paramref name="conflict"/> instead of an exception.
    /// </summary>
    public static async Task<Result> SaveOrConflictAsync(this DbContext db, Error conflict, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return conflict;
        }
    }
}
