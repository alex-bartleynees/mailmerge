using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Contacts.Delete;

internal static class DeleteContactEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapDelete("{contactId:long}", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long contactId, DeleteContactHandler handler, CancellationToken ct) =>
        (await handler.Handle(contactId, ct)).NoContentOrProblem();
}
