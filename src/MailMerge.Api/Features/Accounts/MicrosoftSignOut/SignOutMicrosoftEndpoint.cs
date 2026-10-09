using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.MicrosoftSignOut;

internal static class SignOutMicrosoftEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("{accountId:long}/microsoft/sign-out", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long accountId, SignOutMicrosoftHandler handler, CancellationToken ct) =>
        (await handler.Handle(accountId, ct)).NoContentOrProblem();
}
