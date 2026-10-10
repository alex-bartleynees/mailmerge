using MailMerge.Api.Host;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.AddAttachment;

public sealed class AddAttachmentHandler(MailMergeDbContext db, CurrentUser user, IOptions<AppSettings> settings) : IHandler
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    public async Task<Result<AddAttachmentResponse>> Handle(long campaignId, IFormFile? file, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var editable = campaign.EnsureEditable();
        if (editable.IsFailure)
        {
            return editable.Error;
        }

        if (file is null || file.Length == 0)
        {
            return CampaignErrors.NoFile;
        }

        if (file.Length > settings.Value.MaxUploadBytes)
        {
            return CampaignErrors.AttachmentTooLarge(settings.Value.MaxUploadMegabytes);
        }

        var name = Path.GetFileName(file.FileName).Replace("\0", "").Trim();
        if (name.Length == 0)
        {
            name = "attachment";
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var contentType = !string.IsNullOrWhiteSpace(file.ContentType) && file.ContentType != "application/octet-stream"
            ? file.ContentType
            : ContentTypes.TryGetContentType(name, out var guessed) ? guessed : "application/octet-stream";

        var attachment = new Attachment
        {
            WorkspaceId = user.WorkspaceId,
            CampaignId = campaignId,
            FileName = name.Length > 255 ? name[^255..] : name,
            ContentType = contentType,
            Size = buffer.Length,
            Content = buffer.ToArray(),
        };
        db.Attachments.Add(attachment);
        await db.SaveChangesAsync(ct);

        var total = await db.Attachments.Where(a => a.CampaignId == campaignId).SumAsync(a => a.Size, ct);
        return new AddAttachmentResponse(
            new AttachmentView(attachment.Id, attachment.FileName, attachment.ContentType, attachment.Size, true), total);
    }
}
