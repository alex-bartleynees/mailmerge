using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.Check;

internal static class CheckAccountEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{accountId:long}/check", Handle);

    private static async Task<Results<Ok<CheckAccountResponse>, ProblemHttpResult>> Handle(
        long accountId, CheckAccountHandler handler, CancellationToken ct) =>
        (await handler.Handle(accountId, ct)).OkOrProblem();
}
