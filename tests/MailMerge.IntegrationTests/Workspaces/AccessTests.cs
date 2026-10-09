using System.Net;
using System.Net.Http.Json;
using MailMerge.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MailMerge.IntegrationTests.Workspaces;

[Collection(ApiCollection.Name)]
public sealed class AccessTests(MailMergeApiFactory api)
{
    private sealed record WorkspaceView(long Id, string Name, int RetentionDays);

    private sealed record UserView(string Subject, string? Email);

    private sealed record CurrentResponse(UserView User, WorkspaceView Workspace);

    [Fact]
    public async Task Anonymous_request_is_rejected_with_401()
    {
        var client = api.CreateClientWith(c => c.DefaultRequestHeaders.Add(TestAuthHandler.NoAuthHeader, "1"));

        var response = await client.GetAsync("/api/workspace");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signed_in_user_without_the_mailmerge_role_gets_403()
    {
        // e.g. someone who self-registered in the realm for another app.
        var client = api.CreateClientWith(c => c.DefaultRequestHeaders.Add(TestAuthHandler.RolesHeader, TestAuthHandler.None));

        var response = await client.GetAsync("/api/workspace");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task User_with_role_but_no_workspace_group_gets_a_readable_403()
    {
        var client = api.CreateClientWith(c => c.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, "/other-app/x"));

        var response = await client.GetAsync("/api/workspace");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Contain("isn't linked to a shop");
    }

    [Fact]
    public async Task First_request_creates_the_workspace_from_the_group()
    {
        var client = api.CreateStaffClient("max-wilson-" + Guid.NewGuid().ToString("N")[..6], subject: "alice");

        var current = await client.GetFromJsonAsync<CurrentResponse>("/api/workspace");

        current!.Workspace.Name.Should().StartWith("Max Wilson");
        current.Workspace.RetentionDays.Should().Be(90);
        current.User.Subject.Should().Be("alice");
    }

    [Fact]
    public async Task Concurrent_first_requests_create_one_workspace()
    {
        var workspace = MailMergeApiFactory.NewWorkspace();
        var clients = Enumerable.Range(0, 8).Select(i => api.CreateStaffClient(workspace, $"user-{i}"));

        var results = await Task.WhenAll(clients.Select(c => c.GetFromJsonAsync<CurrentResponse>("/api/workspace")));

        results.Select(r => r!.Workspace.Id).Distinct().Should().ContainSingle();
        var rows = await api.WithDbAsync(db =>
            db.Workspaces.CountAsync(w => w.KeycloakGroup == "/mailmerge/" + workspace));
        rows.Should().Be(1);
    }

    [Fact]
    public async Task User_in_two_workspaces_can_choose_with_the_header()
    {
        var a = MailMergeApiFactory.NewWorkspace();
        var b = MailMergeApiFactory.NewWorkspace();
        var client = api.CreateClientWith(c =>
            c.DefaultRequestHeaders.Add(TestAuthHandler.GroupsHeader, $"/mailmerge/{a},/mailmerge/{b}"));

        client.DefaultRequestHeaders.Add("X-Workspace", b);
        var chosen = await client.GetFromJsonAsync<CurrentResponse>("/api/workspace");

        var direct = await api.CreateStaffClient(b).GetFromJsonAsync<CurrentResponse>("/api/workspace");
        chosen!.Workspace.Id.Should().Be(direct!.Workspace.Id);
    }

    [Fact]
    public async Task Choosing_a_workspace_you_are_not_in_is_forbidden()
    {
        var client = api.CreateStaffClient();
        client.DefaultRequestHeaders.Add("X-Workspace", "someone-elses-shop");

        var response = await client.GetAsync("/api/workspace");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
