using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.OptOuts;

public enum OptOutSource
{
    Manual,
    Link,
    Erasure,
}

/// <summary>
/// Someone who must never be emailed by this workspace. Matching uses <see cref="EmailHash"/>, so the plain
/// <see cref="Email"/> can be removed (privacy erasure) while the person stays opted out.
/// </summary>
public sealed class OptOut : IWorkspaceOwned
{
    public long WorkspaceId { get; set; }
    public required string EmailHash { get; set; }
    public string? Email { get; set; }
    public DateTimeOffset Added { get; set; }
    public string? Reason { get; set; }
    public OptOutSource Source { get; set; }
}

internal sealed class OptOutConfiguration : IEntityTypeConfiguration<OptOut>
{
    public void Configure(EntityTypeBuilder<OptOut> b)
    {
        b.HasKey(o => new { o.WorkspaceId, o.EmailHash });
        b.Property(o => o.EmailHash).HasMaxLength(64);
        b.Property(o => o.Email).HasMaxLength(320);
        b.Property(o => o.Reason).HasMaxLength(500);
        b.Property(o => o.Source).HasConversion<string>().HasMaxLength(20);
    }
}
