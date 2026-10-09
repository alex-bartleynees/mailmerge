using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Campaigns;

/// <summary>
/// A file sent with every message of a campaign. <see cref="Content"/> is large: list queries must project
/// it away. Retention empties <see cref="Content"/> but keeps the name so archived campaigns still show it.
/// </summary>
public sealed class Attachment : IWorkspaceOwned
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }
    public long CampaignId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long Size { get; set; }
    public byte[]? Content { get; set; }
}

internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> b)
    {
        b.HasIndex(a => a.CampaignId);
        b.Property(a => a.FileName).HasMaxLength(255);
        b.Property(a => a.ContentType).HasMaxLength(255);
        b.HasOne<Campaign>().WithMany().HasForeignKey(a => a.CampaignId).OnDelete(DeleteBehavior.Cascade);
    }
}
