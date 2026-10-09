using MailMerge.Api.Features.OptOuts.Add;
using MailMerge.Api.Features.OptOuts.GetAll;
using MailMerge.Api.Features.OptOuts.Remove;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.OptOuts;

public sealed class OptOutsEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/opt-outs", "Opt-outs");
        GetOptOutsEndpoint.Map(group);
        AddOptOutsEndpoint.Map(group);
        RemoveOptOutEndpoint.Map(group);
    }
}
