namespace MailMerge.Api.Features.Accounts.Sending;

/// <summary>
/// One sending account's mailbox. The worker always drafts first and sends the draft second, so a crash between
/// the two never produces a duplicate: the saved draft id is reused.
/// </summary>
public interface IMailSender
{
    bool SupportsDrafts { get; }

    /// <summary>Creates a real draft in the mailbox's Drafts folder and returns its id.</summary>
    Task<string> CreateDraftAsync(OutgoingMessage message, CancellationToken ct);

    /// <summary>Sends a draft created by <see cref="CreateDraftAsync"/>; it then appears in Sent Items.</summary>
    Task SendDraftAsync(string draftId, CancellationToken ct);

    /// <summary>For accounts without drafts (SMTP without IMAP) and test sends.</summary>
    Task SendNowAsync(OutgoingMessage message, CancellationToken ct);

    /// <summary>Verifies the connection and returns a sentence for staff.</summary>
    Task<string> CheckAsync(CancellationToken ct);
}

/// <summary>The account needs attention (signed out, wrong password, permission denied). Campaigns pause.</summary>
public sealed class AccountProblemException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>This message definitely wasn't sent (e.g. the server refused the recipient). It's marked failed.</summary>
public sealed class MessageRejectedException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The send call may or may not have gone through (timeout, dropped connection, gateway error). The message is
/// marked uncertain and never retried automatically: staff check Sent Items first.
/// </summary>
public sealed class DeliveryUncertainException(string message, Exception? inner = null) : Exception(message, inner);
