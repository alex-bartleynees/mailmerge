namespace MailMerge.Api.Features.Workspaces.GetCurrent;

public sealed record GetCurrentWorkspaceResponse(
    GetCurrentWorkspaceResponse.UserView User,
    GetCurrentWorkspaceResponse.WorkspaceView Workspace,
    GetCurrentWorkspaceResponse.ChecklistView Checklist)
{
    public sealed record UserView(string Subject, string? Email, string? Name);

    public sealed record WorkspaceView(long Id, string Name, int RetentionDays);

    /// <summary>Drives the "Getting started" steps in the UI.</summary>
    public sealed record ChecklistView(bool HasAccount, bool AccountConnected, bool HasContacts, bool HasCampaign);
}
