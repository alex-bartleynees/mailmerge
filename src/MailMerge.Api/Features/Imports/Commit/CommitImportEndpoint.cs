using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Imports.Commit;

internal static class CommitImportEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{token:guid}/commit", Handle);

    private static async Task<Results<Ok<CommitImportResponse>, ProblemHttpResult>> Handle(
        Guid token, CommitImportRequest request, CommitImportHandler handler, CancellationToken ct) =>
        (await handler.Handle(token, request, ct)).OkOrProblem();
}
