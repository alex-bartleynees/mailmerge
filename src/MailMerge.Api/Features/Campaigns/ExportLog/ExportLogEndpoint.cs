using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.ExportLog;

internal static class ExportLogEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{campaignId:long}/log.csv", Handle);

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Handle(
        long campaignId, ExportLogHandler handler, CancellationToken ct)
    {
        var result = await handler.Handle(campaignId, ct);
        return result.IsSuccess
            ? TypedResults.File(result.ValueOrThrow.Content, "text/csv; charset=utf-8", result.ValueOrThrow.FileName)
            : result.Error.ToProblem();
    }
}
