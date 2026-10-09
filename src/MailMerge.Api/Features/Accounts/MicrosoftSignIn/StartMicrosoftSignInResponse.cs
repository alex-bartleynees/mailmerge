namespace MailMerge.Api.Features.Accounts.MicrosoftSignIn;

/// <summary>The UI sends the browser to <c>AuthorizeUrl</c>; Microsoft brings it back to the callback.</summary>
public sealed record StartMicrosoftSignInResponse(string AuthorizeUrl);
