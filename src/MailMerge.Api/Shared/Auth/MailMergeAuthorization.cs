using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace MailMerge.Api.Shared.Auth;

public static class MailMergeAuthorization
{
    /// <summary>Signed in and holding the MailMerge client role. Anyone else in the realm gets 403.</summary>
    public const string Policy = "mailmerge";
}

internal sealed class MailMergeRoleRequirement : IAuthorizationRequirement;

internal sealed class MailMergeRoleHandler(IOptions<AuthSettings> settings)
    : AuthorizationHandler<MailMergeRoleRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, MailMergeRoleRequirement requirement)
    {
        var s = settings.Value;
        if (context.User.HasClientRole(s.RoleClientId, s.RequiredRole))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
