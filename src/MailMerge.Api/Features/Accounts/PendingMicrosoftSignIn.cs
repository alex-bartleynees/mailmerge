using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Features.Accounts;

/// <summary>An in-flight "Sign in with Microsoft" redirect. Lives in the database so a restart doesn't lose it.</summary>
public sealed class PendingMicrosoftSignIn
{
    public required string State { get; set; }
    public long AccountId { get; set; }
    public long WorkspaceId { get; set; }
    public required string UserSubject { get; set; }
    public required string ProtectedCodeVerifier { get; set; }
    public DateTimeOffset Expires { get; set; }
}

internal sealed class PendingMicrosoftSignInConfiguration : IEntityTypeConfiguration<PendingMicrosoftSignIn>
{
    public void Configure(EntityTypeBuilder<PendingMicrosoftSignIn> b)
    {
        b.HasKey(s => s.State);
        b.Property(s => s.State).HasMaxLength(100);
    }
}
