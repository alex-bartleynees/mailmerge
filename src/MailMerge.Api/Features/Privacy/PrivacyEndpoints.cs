using MailMerge.Api.Features.Privacy.ErasePerson;
using MailMerge.Api.Features.Privacy.FindPerson;
using MailMerge.Api.Features.Privacy.GetLog;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Privacy;

public sealed class PrivacyEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/privacy", "Privacy");
        FindPersonEndpoint.Map(group);
        ErasePersonEndpoint.Map(group);
        GetPrivacyLogEndpoint.Map(group);
    }
}
