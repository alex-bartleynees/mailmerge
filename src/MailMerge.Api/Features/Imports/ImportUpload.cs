using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Imports;

/// <summary>A spreadsheet uploaded for preview, kept until it's imported or purged after a day.</summary>
public sealed class ImportUpload : IWorkspaceOwned
{
    public Guid Token { get; set; }
    public long WorkspaceId { get; set; }
    public required string FileName { get; set; }
    public required byte[] Content { get; set; }
    public DateTimeOffset Created { get; set; }
}

internal sealed class ImportUploadConfiguration : IEntityTypeConfiguration<ImportUpload>
{
    public void Configure(EntityTypeBuilder<ImportUpload> b)
    {
        b.HasKey(u => u.Token);
        b.Property(u => u.FileName).HasMaxLength(255);
    }
}
