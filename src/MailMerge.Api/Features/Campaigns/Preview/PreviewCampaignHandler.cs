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

public sealed class PreviewCampaignHandler(MailMergeDbContext db, IOptions<AppSettings> settings, DeliveryPlanner planner) : IHandler
{
    public async Task<Result<PreviewCampaignResponse>> Handle(long campaignId, long? contactId, CancellationToken ct)
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

        // Lists are small (hundreds to a few thousand), so filter in memory and page through the people the
        // email's filter includes.
        var all = campaign.ListId is null
            ? []
            : await db.Contacts.AsNoTracking().Where(c => c.ListId == campaign.ListId).OrderBy(c => c.Id).ToListAsync(ct);
        var matching = campaign.Filter is { } filter ? all.Where(c => filter.Matches(c.MergeData())).ToList() : all;
        var contact = contactId is { } id ? matching.FirstOrDefault(c => c.Id == id) : matching.FirstOrDefault();

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
        var workspaceFooter = campaign.IncludeBusinessFooter && campaign.UseWorkspaceBusinessFooter
            ? await db.Workspaces.Where(w => w.Id == campaign.WorkspaceId).Select(w => w.BusinessFooter).SingleAsync(ct)
            : null;
        var body = MessageBuilder.Body(campaign, data, workspaceFooter,
            $"{settings.Value.PublicBaseUrl.TrimEnd('/')}{UnsubscribeLinks.PublicPath}/preview");

        return new PreviewCampaignResponse(
            data.GetValueOrDefault("Email", ""),
            subject,
            body,
            campaign.IsHtml,
            contact?.Id,
            contact is null ? null : matching.LastOrDefault(c => c.Id < contact.Id)?.Id,
            contact is null ? null : matching.FirstOrDefault(c => c.Id > contact.Id)?.Id,
            all.Count,
            matching.Count,
            unknown,
            empty);
    }
}
