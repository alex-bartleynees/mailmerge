using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Privacy;

/// <summary>Proof a privacy action happened. Holds counts only, never an email address.</summary>
public sealed class PrivacyLogEntry : IWorkspaceOwned
{
    public long Id { get; set; }
    public long WorkspaceId { get; set; }

    /// <summary><c>erase</c> or <c>retention_purge</c>.</summary>
    public required string Action { get; set; }

    public required string PerformedBy { get; set; }
    public DateTimeOffset PerformedAt { get; set; }
    public Dictionary<string, int> Counts { get; set; } = new();
}

internal sealed class PrivacyLogEntryConfiguration : IEntityTypeConfiguration<PrivacyLogEntry>
{
    public void Configure(EntityTypeBuilder<PrivacyLogEntry> b)
    {
        b.HasIndex(e => new { e.WorkspaceId, e.PerformedAt });
        b.Property(e => e.Action).HasMaxLength(40);
        b.Property(e => e.PerformedBy).HasMaxLength(320);
        b.Property(e => e.Counts).HasJsonConversion();
    }
}
