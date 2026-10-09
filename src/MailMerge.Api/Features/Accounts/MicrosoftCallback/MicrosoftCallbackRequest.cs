using Microsoft.AspNetCore.Mvc;

namespace MailMerge.Api.Features.Accounts.MicrosoftCallback;

/// <summary>Query parameters Microsoft adds when redirecting back (OAuth 2.0 authorization response).</summary>
public sealed record MicrosoftCallbackRequest(
    [property: FromQuery(Name = "code")] string? Code,
    [property: FromQuery(Name = "state")] string? State,
    [property: FromQuery(Name = "error")] string? Error,
    [property: FromQuery(Name = "error_description")] string? ErrorDescription);
