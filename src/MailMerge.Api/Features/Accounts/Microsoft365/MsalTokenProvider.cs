using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;

namespace MailMerge.Api.Features.Accounts.Microsoft365;

internal sealed class MsalTokenProvider(MicrosoftClientFactory clients, IServiceScopeFactory scopes) : IMicrosoftTokenProvider
{
    public async Task<string> GetAccessTokenAsync(SendingAccount account, CancellationToken ct)
    {
        if (!clients.Settings.IsConfigured)
        {
            throw new AccountProblemException("Microsoft sign-in isn't set up on this server yet (see SETUP.md).");
        }

        if (account.MicrosoftTokenCache is null)
        {
            throw new AccountProblemException("Not signed in to Microsoft. Open Accounts and click Sign in.");
        }

        var (app, cache) = clients.Create(account.MicrosoftTokenCache);
        var user = (account.MicrosoftAccountId is { } id ? await app.GetAccountAsync(id) : null)
                   ?? throw new AccountProblemException("Not signed in to Microsoft. Open Accounts and click Sign in.");

        AuthenticationResult result;
        try
        {
            result = await app.AcquireTokenSilent(MicrosoftSettings.Scopes, user).ExecuteAsync(ct);
        }
        catch (MsalUiRequiredException e)
        {
            throw new AccountProblemException("Microsoft sign-in has expired. Open Accounts and sign in again.", e);
        }
        catch (MsalServiceException e) when (e.StatusCode is 400 or 401 or 403)
        {
            throw new AccountProblemException($"Microsoft refused the sign-in: {e.Message}", e);
        }

        if (cache.ChangedCache is { } changed)
        {
            account.MicrosoftTokenCache = changed;
            await PersistAsync(account.Id, changed, ct);
        }

        return result.AccessToken;
    }

    /// <summary>Saved in its own scope: the caller may not own a DbContext, or may be mid-transaction.</summary>
    private async Task PersistAsync(long accountId, byte[] cache, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MailMergeDbContext>();
        await db.SendingAccounts.IgnoreQueryFilters().Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.MicrosoftTokenCache, cache), ct);
    }
}
