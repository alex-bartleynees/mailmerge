using MailMerge.Api.Features.Campaigns.Delivery;
using MailMerge.Api.Features.Contacts;
using MailMerge.Api.Features.OptOuts.Unsubscribe;
using MailMerge.Api.Host;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Merging;
using MailMerge.Api.Shared.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.Results;

namespace MailMerge.Api.Features.Campaigns.Preview;

public sealed class PreviewCampaignHandler(MailMergeDbContext db, IOptions<AppSettings> settings) : IHandler
{
    public async Task<Result<PreviewCampaignResponse>> Handle(long campaignId, long? contactId, CancellationToken ct)
    {
        var campaign = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(c => c.Id == campaignId, ct);
        if (campaign is null)
        {
            return CampaignErrors.NotFound;
        }

        var contacts = db.Contacts.AsNoTracking().Where(c => c.ListId == campaign.ListId);
        Contact? contact = contactId is { } id
            ? await contacts.SingleOrDefaultAsync(c => c.Id == id, ct)
            : await contacts.OrderBy(c => c.Id).FirstOrDefaultAsync(ct);

        var data = contact?.MergeData() ?? new Dictionary<string, string>();
        var columns = campaign.ListId is null
            ? []
            : await db.ContactLists.Where(l => l.Id == campaign.ListId).Select(l => l.Columns).SingleOrDefaultAsync(ct) ?? [];

        var used = MergeTemplate.UsedFields(campaign.Subject).Concat(MergeTemplate.UsedFields(campaign.Body))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unknown = used.Where(f => !columns.Contains(f, StringComparer.OrdinalIgnoreCase)).Order().ToList();
        var empty = used.Where(f => columns.Contains(f, StringComparer.OrdinalIgnoreCase)
                                    && string.IsNullOrWhiteSpace(MergeTemplate.Lookup(data, f)))
            .Order().ToList();

        var subject = MergeTemplate.Render(campaign.Subject, data, asHtml: false);
        var body = MergeTemplate.Render(campaign.Body, data, campaign.IsHtml);
        if (campaign.UnsubscribeFooter)
        {
            body += MessageBuilder.Footer($"{settings.Value.PublicBaseUrl.TrimEnd('/')}{UnsubscribeLinks.PublicPath}/preview", campaign.IsHtml);
        }

        return new PreviewCampaignResponse(
            data.GetValueOrDefault("Email", ""),
            subject,
            body,
            campaign.IsHtml,
            contact?.Id,
            contact is null ? null : await contacts.Where(c => c.Id < contact.Id).OrderByDescending(c => c.Id)
                .Select(c => (long?)c.Id).FirstOrDefaultAsync(ct),
            contact is null ? null : await contacts.Where(c => c.Id > contact.Id).OrderBy(c => c.Id)
                .Select(c => (long?)c.Id).FirstOrDefaultAsync(ct),
            campaign.ListId is null ? 0 : await contacts.CountAsync(ct),
            unknown,
            empty);
    }
}
