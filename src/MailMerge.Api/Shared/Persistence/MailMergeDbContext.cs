using System.Linq.Expressions;
using MailMerge.Api.Features.Accounts;
using MailMerge.Api.Features.Campaigns;
using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Features.Imports;
using MailMerge.Api.Features.Lists;
using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Features.Privacy;
using MailMerge.Api.Features.Workspaces;
using MailMerge.Api.Shared.Auth;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.Api.Shared.Persistence;

/// <summary>
/// Every <see cref="IWorkspaceOwned"/> entity is filtered to <see cref="CurrentUser.WorkspaceId"/>. Outside a
/// request (the worker, retention) that id is 0, so queries return nothing unless they call
/// <c>IgnoreQueryFilters()</c> deliberately.
/// </summary>
public sealed class MailMergeDbContext(DbContextOptions<MailMergeDbContext> options, CurrentUser currentUser)
    : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<SendingAccount> SendingAccounts => Set<SendingAccount>();
    public DbSet<MicrosoftSignIn> MicrosoftSignIns => Set<MicrosoftSignIn>();
    public DbSet<ContactList> ContactLists => Set<ContactList>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ImportUpload> ImportUploads => Set<ImportUpload>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<OptOut> OptOuts => Set<OptOut>();
    public DbSet<PrivacyLogEntry> PrivacyLog => Set<PrivacyLogEntry>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Read by the query filters; EF parameterises it per context instance.</summary>
    internal long CurrentWorkspaceId => currentUser.WorkspaceId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MailMergeDbContext).Assembly);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IWorkspaceOwned).IsAssignableFrom(entity.ClrType))
            {
                entity.SetQueryFilter(WorkspaceFilter(entity.ClrType));
            }
        }
    }

    private LambdaExpression WorkspaceFilter(Type entityType)
    {
        var row = Expression.Parameter(entityType, "row");
        var body = Expression.Equal(
            Expression.Property(row, nameof(IWorkspaceOwned.WorkspaceId)),
            Expression.Property(Expression.Constant(this), nameof(CurrentWorkspaceId)));
        return Expression.Lambda(body, row);
    }
}
