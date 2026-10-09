using SharedKernel.Results;

namespace MailMerge.Api.Features.Contacts;

public static class ContactErrors
{
    public static readonly Error NotFound = Error.NotFound("Contact.NotFound", "Contact not found");

    public static readonly Error EmailRequired = Error.Validation("Contact.EmailRequired", "Enter an email address");

    public static readonly Error EmailRequiredOnEdit =
        Error.Validation("Contact.EmailRequired", "Email can't be empty. Delete the row instead.");

    public static readonly Error Duplicate = Error.Conflict("Contact.Duplicate", "That email is already in this list");

    public static readonly Error DuplicateOnEdit =
        Error.Conflict("Contact.Duplicate", "Another contact in this list already has that email");
}
