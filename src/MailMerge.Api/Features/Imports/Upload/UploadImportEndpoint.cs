using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Imports.Upload;

internal static class UploadImportEndpoint
{
    // Bearer-token API: CSRF is enforced by the BFF in front of it.
    public static void Map(RouteGroupBuilder group) => group.MapPost("", Handle).DisableAntiforgery();

    private static async Task<Results<Ok<ImportPreview>, ProblemHttpResult>> Handle(
        IFormFile? file, UploadImportHandler handler, CancellationToken ct) =>
        (await handler.Handle(file, ct)).OkOrProblem();
}
