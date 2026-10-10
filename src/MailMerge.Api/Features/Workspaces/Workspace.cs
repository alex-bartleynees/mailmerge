using Microsoft.EntityFrameworkCore;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Workspaces;

/// <summary>One shop/business. Created on first sign-in from the user's Keycloak workspace group.</summary>
public sealed class Workspace
{
    public const int DefaultRetentionDays = 90;

    public long Id { get; set; }
    public required string KeycloakGroup { get; set; }
    public required string Name { get; set; }

    /// <summary>Days after a campaign's last activity before its per-recipient rows and attachments are purged.</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Reusable business footer. Emails must opt in to include it.</summary>
    public BusinessFooter? BusinessFooter { get; set; }

    /// <summary>Random per-workspace key for hashing opted-out addresses (see <c>OptOutHasher</c>).</summary>
    public required byte[] OptOutHashKey { get; set; }

    public DateTimeOffset Created { get; set; }
}

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> b)
    {
        b.HasIndex(w => w.KeycloakGroup).IsUnique();
        b.Property(w => w.KeycloakGroup).HasMaxLength(255);
        b.Property(w => w.Name).HasMaxLength(200);
        b.Property(w => w.BusinessFooter).HasJsonConversion();
    }
}
