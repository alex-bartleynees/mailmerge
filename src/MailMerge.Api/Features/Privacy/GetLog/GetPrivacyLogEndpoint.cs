using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Privacy.GetLog;

internal static class GetPrivacyLogEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("log", Handle);

    private static async Task<Ok<IReadOnlyList<PrivacyLogView>>> Handle(GetPrivacyLogHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(ct));
}
