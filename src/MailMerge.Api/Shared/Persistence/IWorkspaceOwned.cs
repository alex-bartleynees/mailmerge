namespace MailMerge.Api.Shared.Persistence;

/// <summary>Rows belonging to one workspace. <see cref="MailMergeDbContext"/> filters them to the current workspace.</summary>
public interface IWorkspaceOwned
{
    long WorkspaceId { get; set; }
}
