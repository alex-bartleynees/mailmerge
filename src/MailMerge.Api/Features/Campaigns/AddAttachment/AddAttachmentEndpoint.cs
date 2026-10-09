using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.AddAttachment;

internal static class AddAttachmentEndpoint
{
    // Bearer-token API: CSRF is enforced by the BFF in front of it.
    public static void Map(RouteGroupBuilder group) =>
        group.MapPost("{campaignId:long}/attachments", Handle).DisableAntiforgery();

    private static async Task<Results<Ok<AddAttachmentResponse>, ProblemHttpResult>> Handle(
        long campaignId, IFormFile? file, AddAttachmentHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, file, ct)).OkOrProblem();
}
