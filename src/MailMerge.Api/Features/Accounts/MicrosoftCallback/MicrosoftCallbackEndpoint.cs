using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;

namespace MailMerge.Api.Features.Accounts.MicrosoftCallback;

internal static class MicrosoftCallbackEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("callback", Handle);

    /// <summary>A browser navigation, so the answer is a redirect back into the app with the outcome in the URL.</summary>
    private static async Task<RedirectHttpResult> Handle(
        [AsParameters] MicrosoftCallbackRequest request, ClaimsPrincipal user, MicrosoftCallbackHandler handler, CancellationToken ct)
    {
        var result = await handler.Handle(request, user, ct);
        var query = result.IsSuccess
            ? new Dictionary<string, string?> { ["microsoft"] = "connected", ["account"] = result.ValueOrThrow.ToString() }
            : new Dictionary<string, string?> { ["microsoft"] = "error", ["message"] = result.Error.Detail };
        return TypedResults.Redirect(QueryHelpers.AddQueryString("/", query) + "#accounts");
    }
}
