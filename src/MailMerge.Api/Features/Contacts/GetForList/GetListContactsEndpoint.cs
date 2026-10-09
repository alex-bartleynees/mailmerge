using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Contacts.GetForList;

internal static class GetListContactsEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("", Handle);

    private static async Task<Results<Ok<GetListContactsResponse>, ProblemHttpResult>> Handle(
        long listId, GetListContactsHandler handler, CancellationToken ct) =>
        (await handler.Handle(listId, ct)).OkOrProblem();
}
