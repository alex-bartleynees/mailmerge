using MailMerge.Api.Features.Imports.Commit;
using MailMerge.Api.Features.Imports.GetSheet;
using MailMerge.Api.Features.Imports.Upload;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Imports;

public sealed class ImportsEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/imports", "Imports");
        UploadImportEndpoint.Map(group);
        GetImportSheetEndpoint.Map(group);
        CommitImportEndpoint.Map(group);
    }
}
