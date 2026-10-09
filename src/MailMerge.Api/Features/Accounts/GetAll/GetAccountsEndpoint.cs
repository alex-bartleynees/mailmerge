using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.GetAll;

internal static class GetAccountsEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("", Handle);

    private static async Task<Ok<IReadOnlyList<AccountView>>> Handle(GetAccountsHandler handler, CancellationToken ct) =>
        TypedResults.Ok(await handler.Handle(ct));
}
