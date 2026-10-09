using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Contacts;

/// <summary>A row in the contacts grid. <c>Valid</c> and <c>OptedOut</c> explain why someone would be skipped.</summary>
public sealed record ContactView(
    long Id,
    string Email,
    bool DoNotEmail,
    IReadOnlyDictionary<string, string> Data,
    bool Valid,
    bool OptedOut)
{
    public static ContactView From(Contact contact, OptOutMatcher optOuts) =>
        new(contact.Id, contact.Email, contact.DoNotEmail, contact.MergeData(),
            EmailAddress.IsValid(contact.Email), optOuts.IsOptedOut(contact.Email));
}
