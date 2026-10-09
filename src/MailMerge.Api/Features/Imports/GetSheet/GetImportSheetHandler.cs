using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Imports.GetSheet;

/// <summary>Re-previews an upload with a different sheet chosen.</summary>
public sealed class GetImportSheetHandler(MailMergeDbContext db) : IHandler
{
    public async Task<Result<ImportPreview>> Handle(Guid token, string? sheet, CancellationToken ct)
    {
        var upload = await db.ImportUploads.AsNoTracking().SingleOrDefaultAsync(u => u.Token == token, ct);
        return upload is null ? ImportErrors.Expired : ImportPreview.Build(upload, sheet);
    }
}
