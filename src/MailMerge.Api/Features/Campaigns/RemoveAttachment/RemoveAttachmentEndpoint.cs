using MailMerge.Api.Shared.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailMerge.Api.Features.Campaigns.RemoveAttachment;

internal static class RemoveAttachmentEndpoint
{
    public static void Map(RouteGroupBuilder group) =>
        group.MapDelete("{campaignId:long}/attachments/{attachmentId:long}", Handle);

    private static async Task<Results<NoContent, ProblemHttpResult>> Handle(
        long campaignId, long attachmentId, RemoveAttachmentHandler handler, CancellationToken ct) =>
        (await handler.Handle(campaignId, attachmentId, ct)).NoContentOrProblem();
}
