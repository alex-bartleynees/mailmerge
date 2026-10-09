using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.Delete;

internal static class DeleteAccountEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapDelete("{accountId:long}", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long accountId, DeleteAccountHandler handler, CancellationToken ct) =>
        (await handler.Handle(accountId, ct)).NoContentOrProblem();
}
