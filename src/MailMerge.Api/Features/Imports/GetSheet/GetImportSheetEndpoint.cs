using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Imports.GetSheet;

internal static class GetImportSheetEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{token:guid}", Handle);

    private static async Task<Results<Ok<ImportPreview>, ProblemHttpResult>> Handle(
        Guid token, string? sheet, GetImportSheetHandler handler, CancellationToken ct) =>
        (await handler.Handle(token, sheet, ct)).OkOrProblem();
}
