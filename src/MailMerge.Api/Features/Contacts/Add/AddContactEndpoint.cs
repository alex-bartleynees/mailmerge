using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Contacts.Add;

internal static class AddContactEndpoint
{
    public static void Map(RouteGroupBuilder group) => group.MapPost("", Handle);

    private static async Task<Results<Created<ContactView>, ProblemHttpResult>> Handle(
        long listId, AddContactRequest request, AddContactHandler handler, CancellationToken ct) =>
        (await handler.Handle(listId, request, ct)).CreatedOrProblem(c => $"/api/contacts/{c.Id}");
}
