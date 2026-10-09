using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.OptOuts.Unsubscribe;

internal static class UnsubscribeEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup(UnsubscribeLinks.PublicPath).AllowAnonymous().WithTags("Public").ExcludeFromDescription();
        group.MapGet("{token}", Show);
        // Also the RFC 8058 one-click target: mail clients POST "List-Unsubscribe=One-Click" here.
        group.MapPost("{token}", Confirm).DisableAntiforgery();
    }

    private static async Task<ContentHttpResult> Show(string token, UnsubscribeHandler handler, CancellationToken ct)
    {
        var result = await handler.Show(token, ct);
        return result.IsSuccess
            ? Html(UnsubscribePages.Confirm(result.ValueOrThrow), StatusCodes.Status200OK)
            : Html(UnsubscribePages.Invalid(result.Error.Detail), StatusCodes.Status404NotFound);
    }

    private static async Task<ContentHttpResult> Confirm(string token, UnsubscribeHandler handler, CancellationToken ct)
    {
        var result = await handler.Confirm(token, ct);
        return result.IsSuccess
            ? Html(UnsubscribePages.Done(result.ValueOrThrow), StatusCodes.Status200OK)
            : Html(UnsubscribePages.Invalid(result.Error.Detail), StatusCodes.Status404NotFound);
    }

    private static ContentHttpResult Html(string html, int status) =>
        TypedResults.Content(html, "text/html; charset=utf-8", statusCode: status);
}
