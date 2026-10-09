namespace MailMerge.Api.Features.Accounts.Check;

/// <summary><c>Ok = false</c> is a normal answer (wrong password, not signed in), not an API error.</summary>
public sealed record CheckAccountResponse(bool Ok, string Message);
