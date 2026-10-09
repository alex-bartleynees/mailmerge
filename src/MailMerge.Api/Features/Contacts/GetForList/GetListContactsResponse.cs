namespace MailMerge.Api.Features.Contacts.GetForList;

public sealed record GetListContactsResponse(long Id, string Name, IReadOnlyList<string> Columns, IReadOnlyList<ContactView> Contacts);
