namespace MailMerge.Api.Features.Contacts.Update;

/// <summary>Either part may be omitted. <c>Data</c>, when present, replaces the whole row.</summary>
public sealed record UpdateContactRequest(Dictionary<string, string?>? Data, bool? DoNotEmail);
