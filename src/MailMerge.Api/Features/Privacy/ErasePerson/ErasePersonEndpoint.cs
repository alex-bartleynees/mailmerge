using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Privacy.ErasePerson;

internal static class ErasePersonEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("people/erase", Handle);

    private static async Task<Results<Ok<ErasePersonResponse>, ProblemHttpResult>> Handle(
        ErasePersonRequest request, ErasePersonHandler handler, CancellationToken ct) =>
        (await handler.Handle(request, ct)).OkOrProblem();
}
