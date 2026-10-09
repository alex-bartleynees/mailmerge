using MailMerge.Api.Host;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Imports.Upload;

public sealed class UploadImportHandler(
    MailMergeDbContext db,
    CurrentUser user,
    TimeProvider clock,
    IOptions<AppSettings> settings) : IHandler
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public async Task<Result<ImportPreview>> Handle(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return ImportErrors.NoFile;
        }

        if (!SpreadsheetReader.IsSupported(file.FileName))
        {
            return ImportErrors.UnsupportedFile;
        }

        if (file.Length > settings.Value.MaxUploadBytes)
        {
            return ImportErrors.TooLarge(settings.Value.MaxUploadMegabytes);
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var upload = new ImportUpload
        {
            Token = Guid.CreateVersion7(),
            WorkspaceId = user.WorkspaceId,
            FileName = Path.GetFileName(file.FileName),
            Content = buffer.ToArray(),
            Created = clock.GetUtcNow(),
        };

        var preview = ImportPreview.Build(upload, sheet: null);
        if (preview.IsFailure)
        {
            return preview;
        }

        var stale = clock.GetUtcNow() - Lifetime;
        await db.ImportUploads.Where(u => u.Created < stale).ExecuteDeleteAsync(ct);
        db.ImportUploads.Add(upload);
        await db.SaveChangesAsync(ct);
        return preview;
    }
}
