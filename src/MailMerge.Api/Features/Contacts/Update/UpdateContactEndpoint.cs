using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Contacts.Update;

internal static class UpdateContactEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPut("{contactId:long}", Handle);

    private static async Task<Results<Ok<ContactView>, ProblemHttpResult>> Handle(
        long contactId, UpdateContactRequest request, UpdateContactHandler handler, CancellationToken ct) =>
        (await handler.Handle(contactId, request, ct)).OkOrProblem();
}
