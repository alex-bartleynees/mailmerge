using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Privacy.FindPerson;

internal static class FindPersonEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("people", Handle);

    private static async Task<Results<Ok<PersonRecords>, ProblemHttpResult>> Handle(
        string? email, FindPersonHandler handler, CancellationToken ct) =>
        (await handler.Handle(email, ct)).OkOrProblem();
}
