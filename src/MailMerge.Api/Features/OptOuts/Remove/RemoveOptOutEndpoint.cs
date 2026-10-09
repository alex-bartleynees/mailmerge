using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.OptOuts.Remove;

internal static class RemoveOptOutEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapDelete("{id}", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        string id, RemoveOptOutHandler handler, CancellationToken ct) =>
        (await handler.Handle(id, ct)).NoContentOrProblem();
}
