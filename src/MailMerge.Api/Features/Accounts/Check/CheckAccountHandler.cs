using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Accounts.Check;

public sealed class CheckAccountHandler(
    MailMergeDbContext db,
    IMailSenderFactory senders,
    ILogger<CheckAccountHandler> log) : IHandler
{
    public async Task<Result<CheckAccountResponse>> Handle(long accountId, CancellationToken ct)
    {
        var account = await db.SendingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null)
        {
            return AccountErrors.NotFound;
        }

        try
        {
            return new CheckAccountResponse(true, await senders.Create(account).CheckAsync(ct));
        }
        catch (Exception e) when (e is AccountProblemException or MessageRejectedException or DeliveryUncertainException)
        {
            return new CheckAccountResponse(false, e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Account check failed for {AccountId}", accountId);
            return new CheckAccountResponse(false, $"Couldn't connect: {e.Message}");
        }
    }
}
