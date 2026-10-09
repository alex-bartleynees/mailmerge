using MailMerge.Api.Features.Lists;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Contacts;

public sealed class Contact : IWorkspaceOwned
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public long ListId { get; set; }

    /// <summary>Trimmed and lower-cased. Unique within a list.</summary>
    public required string Email { get; set; }

    /// <summary>Every column value, keyed by the list's column names (including "Email").</summary>
    public Dictionary<string, string> Data { get; set; } = new();

    /// <summary>Staff ticked "Don't email" for this person on this list.</summary>
    public bool DoNotEmail { get; set; }

    /// <summary>Merge data with the Email column guaranteed present.</summary>
    public IReadOnlyDictionary<string, string> MergeData()
    {
        var data = new Dictionary<string, string>(Data);
        data.TryAdd(ContactList.EmailColumn, Email);
        return data;
    }
}

internal sealed class ContactConfiguration : IEntityTypeConfiguration<Contact>
{
    public void Configure(EntityTypeBuilder<Contact> b)
    {
        b.HasIndex(c => new { c.ListId, c.Email }).IsUnique();
        b.HasIndex(c => new { c.WorkspaceId, c.Email });
        b.Property(c => c.Email).HasMaxLength(320);
        b.Property(c => c.Data).HasJsonConversion();
        b.HasOne<ContactList>().WithMany().HasForeignKey(c => c.ListId).OnDelete(DeleteBehavior.Cascade);
    }
}
