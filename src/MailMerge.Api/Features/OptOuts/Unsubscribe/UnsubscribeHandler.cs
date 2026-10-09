using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.OptOuts.Unsubscribe;

/// <summary>
/// The public side of an unsubscribe link. Showing the page changes nothing (mail scanners follow links); only the
/// confirm POST, or a mail client's one-click POST, adds the opt-out.
/// </summary>
public sealed class UnsubscribeHandler(
    MailMergeDbContext db,
    UnsubscribeLinks links,
    OptOutRegistry registry,
    TimeProvider clock) : IHandler
{
    public static readonly Error InvalidLink = Error.NotFound("Unsubscribe.InvalidLink",
        "This unsubscribe link isn't valid. It may have been copied incompletely.");

    public sealed record Page(string ShopName, string MaskedEmail, bool AlreadyUnsubscribed);

    public async Task<Result<Page>> Show(string token, CancellationToken ct)
    {
        if (links.Read(token) is not { } target || await ShopNameAsync(target.WorkspaceId, ct) is not { } shop)
        {
            return InvalidLink;
        }

        return new Page(shop, Mask(target.Email), await registry.IsOptedOutAsync(target.WorkspaceId, target.Email, ct));
    }

    public async Task<Result<Page>> Confirm(string token, CancellationToken ct)
    {
        if (links.Read(token) is not { } target || await ShopNameAsync(target.WorkspaceId, ct) is not { } shop)
        {
            return InvalidLink;
        }

        var hash = (await registry.HasherAsync(target.WorkspaceId, ct))(target.Email);
        var exists = await db.OptOuts.IgnoreQueryFilters()
            .AnyAsync(o => o.WorkspaceId == target.WorkspaceId && o.EmailHash == hash, ct);
        if (!exists)
        {
            db.OptOuts.Add(new OptOut
            {
                WorkspaceId = target.WorkspaceId,
                EmailHash = hash,
                Email = EmailAddress.Normalize(target.Email),
                Added = clock.GetUtcNow(),
                Reason = "Unsubscribe link",
                Source = OptOutSource.Link,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Clicked twice at once: the other request already added it.
            }
        }

        return new Page(shop, Mask(target.Email), AlreadyUnsubscribed: true);
    }

    private Task<string?> ShopNameAsync(long workspaceId, CancellationToken ct) =>
        db.Workspaces.Where(w => w.Id == workspaceId).Select(w => w.Name).SingleOrDefaultAsync(ct);

    /// <summary><c>annie@example.com</c> → <c>a****@example.com</c>: enough to recognise, not to harvest.</summary>
    internal static string Mask(string email)
    {
        var at = email.IndexOf('@');
        return at <= 1 ? email : email[0] + new string('*', at - 1) + email[at..];
    }
}
