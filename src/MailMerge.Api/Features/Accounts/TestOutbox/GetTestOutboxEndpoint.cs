using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Accounts.TestOutbox;

internal static class GetTestOutboxEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapGet("{accountId:long}/test-outbox", Handle);

    private static async Task<Results<Ok<TestOutboxResponse>, ProblemHttpResult>> Handle(
        long accountId, GetTestOutboxHandler handler, CancellationToken ct) =>
        (await handler.Handle(accountId, ct)).OkOrProblem();
}
