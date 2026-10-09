using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Lists.Export;

internal static class ExportListEndpoint
{
    private const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static void Map(RouteGroupBuilder group) => group.MapGet("{listId:long}/export", Handle);

    private static async Task<Results<FileContentHttpResult, ProblemHttpResult>> Handle(
        long listId, ExportListHandler handler, CancellationToken ct)
    {
        var result = await handler.Handle(listId, ct);
        return result.IsSuccess
            ? TypedResults.File(result.ValueOrThrow.Content, XlsxContentType, result.ValueOrThrow.FileName)
            : result.Error.ToProblem();
    }
}
