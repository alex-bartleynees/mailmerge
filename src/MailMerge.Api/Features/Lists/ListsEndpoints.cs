using MailMerge.Api.Features.Lists.AddColumn;
using MailMerge.Api.Features.Lists.Create;
using MailMerge.Api.Features.Lists.Delete;
using MailMerge.Api.Features.Lists.Export;
using MailMerge.Api.Features.Lists.GetAll;
using MailMerge.Api.Features.Lists.RemoveColumn;
using MailMerge.Api.Features.Lists.Rename;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Lists;

public sealed class ListsEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/lists", "Lists");
        GetListsEndpoint.Map(group);
        CreateListEndpoint.Map(group);
        RenameListEndpoint.Map(group);
        DeleteListEndpoint.Map(group);
        AddColumnEndpoint.Map(group);
        RemoveColumnEndpoint.Map(group);
        ExportListEndpoint.Map(group);
    }
}
