namespace MailMerge.Api.Features.Accounts.Microsoft365;

public interface IMicrosoftTokenProvider
{
    /// <summary>A Graph access token for the mailbox signed in on <paramref name="account"/>.</summary>
    /// <exception cref="Sending.AccountProblemException">Not signed in, or the sign-in has expired.</exception>
    Task<string> GetAccessTokenAsync(SendingAccount account, CancellationToken ct);
}
