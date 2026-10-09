using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.Create;

internal static class CreateAccountEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("", Handle);

    private static async Task<Created<AccountView>> Handle(
        CreateAccountRequest request, CreateAccountHandler handler, CancellationToken ct)
    {
        var account = await handler.Handle(request, ct);
        return TypedResults.Created($"/api/accounts/{account.Id}", account);
    }
}
