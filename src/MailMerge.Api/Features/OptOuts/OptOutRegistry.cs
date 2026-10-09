using System.Security.Cryptography;
using System.Text;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Features.OptOuts;

/// <summary>
/// The one place opted-out addresses are hashed and matched. Takes an explicit workspace id so the worker and
/// retention (which run outside a request) use it the same way endpoints do.
/// </summary>
public sealed class OptOutRegistry(MailMergeDbContext db)
{
    public async Task<OptOutMatcher> LoadAsync(long workspaceId, CancellationToken ct)
    {
        var key = await KeyAsync(workspaceId, ct);
        var hashes = await db.OptOuts.IgnoreQueryFilters()
            .Where(o => o.WorkspaceId == workspaceId)
            .Select(o => o.EmailHash)
            .ToListAsync(ct);
        return new OptOutMatcher(key, hashes.ToHashSet(StringComparer.Ordinal));
    }

    public async Task<bool> IsOptedOutAsync(long workspaceId, string email, CancellationToken ct)
    {
        var hash = Hash(await KeyAsync(workspaceId, ct), email);
        return await db.OptOuts.IgnoreQueryFilters().AnyAsync(o => o.WorkspaceId == workspaceId && o.EmailHash == hash, ct);
    }

    public async Task<Func<string, string>> HasherAsync(long workspaceId, CancellationToken ct)
    {
        var key = await KeyAsync(workspaceId, ct);
        return email => Hash(key, email);
    }

    /// <summary>HMAC-SHA256 of the normalised address, hex. Keyed per workspace so hashes don't match across shops.</summary>
    public static string Hash(byte[] key, string email) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(EmailAddress.Normalize(email))));

    private Task<byte[]> KeyAsync(long workspaceId, CancellationToken ct) =>
        db.Workspaces.AsNoTracking().Where(w => w.Id == workspaceId).Select(w => w.OptOutHashKey).SingleAsync(ct);
}

public sealed class OptOutMatcher(byte[] key, IReadOnlySet<string> hashes)
{
    public string Hash(string email) => OptOutRegistry.Hash(key, email);

    public bool IsOptedOut(string email) => hashes.Contains(Hash(email));
}
