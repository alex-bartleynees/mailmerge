namespace MailMerge.Api.Features.Contacts.Add;

/// <summary>Column name → value. Must include "Email"; unknown columns are added to the list.</summary>
public sealed record AddContactRequest(Dictionary<string, string?> Data);
