using MailMerge.Api.Features.Lists;
using MailMerge.Api.Shared.Merging;

namespace MailMerge.Api.Features.Contacts;

internal static class ContactData
{
    /// <summary>
    /// Trims values and pulls out the email (the "Email" key, any case). The stored data always uses the exact
    /// "Email" key holding the normalised address.
    /// </summary>
    public static (string Email, Dictionary<string, string> Data) Clean(IReadOnlyDictionary<string, string?> input)
    {
        var data = new Dictionary<string, string>();
        var email = "";
        foreach (var (key, value) in input)
        {
            var k = key.Trim();
            if (k.Length == 0)
            {
                continue;
            }

            if (string.Equals(k, ContactList.EmailColumn, StringComparison.OrdinalIgnoreCase))
            {
                email = EmailAddress.Normalize(value);
                continue;
            }

            data[k] = (value ?? "").Trim();
        }

        data[ContactList.EmailColumn] = email;
        return (email, data);
    }

    /// <summary>Adds columns the list doesn't have yet (case-insensitive), keeping the existing order.</summary>
    public static List<string> MergeColumns(IReadOnlyList<string> columns, IEnumerable<string> keys)
    {
        var merged = columns.ToList();
        foreach (var key in keys)
        {
            if (!merged.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                merged.Add(key);
            }
        }

        return merged;
    }
}
