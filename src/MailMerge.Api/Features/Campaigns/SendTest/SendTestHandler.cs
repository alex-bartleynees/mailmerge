using MailMerge.Api.Features.Accounts.Sending;
using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.SendTest;

/// <summary>
/// Sends one "[TEST]" copy to a staff member's own address, merged with the first included contact's details, so they can
/// see exactly what customers will get. Sent straight away; it doesn't touch the campaign's progress.
/// </summary>
public sealed class SendTestHandler(
    MailMergeDbContext db,
    DeliveryPlanner planner,
    IMailSenderFactory senders,
    ILogger<SendTestHandler> log) : IHandler
{
    public static readonly Error NoAccount = Error.Validation("Campaign.NoAccount", "Choose a sending account first");

    public async Task<Result<SendTestResponse>> Handle(long campaignId, SendTestRequest request, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        if (await planner.FilterErrorAsync(campaign, ct) is { } filterError)
        {
            return filterError;
        }

        var account = campaign.AccountId is { } accountId
            ? await db.SendingAccounts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == accountId, ct)
            : null;
        if (account is null)
        {
            return NoAccount;
        }

        var to = request.To.Trim();
        var contacts = await db.Contacts.AsNoTracking().Where(c => c.ListId == campaign.ListId).OrderBy(c => c.Id)
            .ToListAsync(ct);
        var first = contacts.FirstOrDefault(c => campaign.Filter?.Matches(c.MergeData()) ?? true);
        var data = first?.MergeData() ?? new Dictionary<string, string> { ["Email"] = to };

        try
        {
            var message = MessageBuilder.Build(campaign, data, to, await CampaignAttachments.LoadAsync(db, campaign, ct),
                unsubscribeUrl: null, MessageBuilder.TestSubjectPrefix);
            await senders.Create(account).SendNowAsync(message, ct);
        }
        catch (Exception e) when (e is AccountProblemException or MessageRejectedException or DeliveryUncertainException
                                      or EmptySubjectException)
        {
            return Error.Validation("Campaign.TestFailed", e.Message);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "Test send failed for campaign {CampaignId}", campaignId);
            return Error.Validation("Campaign.TestFailed", $"Test failed: {e.Message}");
        }

        return new SendTestResponse(first is null
            ? $"Test sent to {to}."
            : $"Test sent to {to} using the first contact's details.");
    }
}
