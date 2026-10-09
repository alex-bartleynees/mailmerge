using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.MicrosoftSignIn;

internal static class StartMicrosoftSignInEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{accountId:long}/microsoft/sign-in", Handle);

    private static async Task<Results<Ok<StartMicrosoftSignInResponse>, ProblemHttpResult>> Handle(
        long accountId, StartMicrosoftSignInHandler handler, CancellationToken ct) =>
        (await handler.Handle(accountId, ct)).OkOrProblem();
}
