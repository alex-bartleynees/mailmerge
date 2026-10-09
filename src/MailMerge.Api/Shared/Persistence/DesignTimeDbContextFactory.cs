using MailMerge.Api.Shared.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MailMerge.Api.Shared.Persistence;

/// <summary>Used only by <c>dotnet ef</c>; no database connection is made when adding migrations.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MailMergeDbContext>
{
    public MailMergeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MailMergeDbContext>()
            .UseNpgsql("Host=localhost;Database=mailmerge_design")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new MailMergeDbContext(options, new CurrentUser());
    }
}
