using MailMerge.Api.Features.Contacts.Add;
using MailMerge.Api.Features.Contacts.Delete;
using MailMerge.Api.Features.Contacts.GetForList;
using MailMerge.Api.Features.Contacts.Update;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Contacts;

public sealed class ContactsEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var inList = app.MapStaffGroup("/api/lists/{listId:long}/contacts", "Contacts");
        GetListContactsEndpoint.Map(inList);
        AddContactEndpoint.Map(inList);

        var contacts = app.MapStaffGroup("/api/contacts", "Contacts");
        UpdateContactEndpoint.Map(contacts);
        DeleteContactEndpoint.Map(contacts);
    }
}
