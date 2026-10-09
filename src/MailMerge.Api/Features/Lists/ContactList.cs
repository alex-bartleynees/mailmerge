using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Lists;

public sealed class ContactList : IWorkspaceOwned
{
    public const string EmailColumn = "Email";

    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public required string Name { get; set; }

    /// <summary>Column names in display order; always starts with <see cref="EmailColumn"/>.</summary>
    public List<string> Columns { get; set; } = [EmailColumn];

    public DateTimeOffset Created { get; set; }
}

internal sealed class ContactListConfiguration : IEntityTypeConfiguration<ContactList>
{
    public void Configure(EntityTypeBuilder<ContactList> b)
    {
        b.HasIndex(l => l.WorkspaceId);
        b.Property(l => l.Name).HasMaxLength(200);
    }
}
