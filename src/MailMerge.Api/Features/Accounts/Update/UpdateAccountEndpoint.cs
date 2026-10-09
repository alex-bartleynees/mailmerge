using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.Update;

internal static class UpdateAccountEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPut("{accountId:long}", Handle);

    private static async Task<Results<Ok<AccountView>, ProblemHttpResult>> Handle(
        long accountId, UpdateAccountRequest request, UpdateAccountHandler handler, CancellationToken ct) =>
        (await handler.Handle(accountId, request, ct)).OkOrProblem();
}
