using MailMerge.Api.Features.OptOuts;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Privacy.FindPerson;

/// <summary>Shows what's held about someone before deleting it, so staff can tell them.</summary>
public sealed class FindPersonHandler(MailMergeDbContext db, OptOutRegistry registry, CurrentUser user) : IHandler
{
    public async Task<Result<PersonRecords>> Handle(string? email, CancellationToken ct)
    {
        if (!EmailAddress.IsValid(email))
        {
            return PrivacyErrors.InvalidEmail;
        }

        var hash = (await registry.HasherAsync(user.WorkspaceId, ct))(email!);
        return await PersonRecords.FindAsync(db, hash, email!, ct);
    }
}
