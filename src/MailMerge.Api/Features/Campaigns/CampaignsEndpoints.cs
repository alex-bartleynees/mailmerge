using MailMerge.Api.Features.Campaigns.AddAttachment;
using MailMerge.Api.Features.Campaigns.Copy;
using MailMerge.Api.Features.Campaigns.Create;
using MailMerge.Api.Features.Campaigns.Delete;
using MailMerge.Api.Features.Campaigns.Get;
using MailMerge.Api.Features.Campaigns.GetAll;
using MailMerge.Api.Features.Campaigns.GetPlan;
using MailMerge.Api.Features.Campaigns.Preview;
using MailMerge.Api.Features.Campaigns.RemoveAttachment;
using MailMerge.Api.Features.Campaigns.SendTest;
using MailMerge.Api.Features.Campaigns.ExportLog;
using MailMerge.Api.Features.Campaigns.MarkMessage;
using MailMerge.Api.Features.Campaigns.Messages;
using MailMerge.Api.Features.Campaigns.Pause;
using MailMerge.Api.Features.Campaigns.RetryFailed;
using MailMerge.Api.Features.Campaigns.Run;
using MailMerge.Api.Features.Campaigns.Update;
using MailMerge.Api.Shared.Http;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Features.Campaigns;

public sealed class CampaignsEndpoints : IEndpointDefinition
{
    public void RegisterEndpoints(WebApplication app)
    {
        var group = app.MapStaffGroup("/api/campaigns", "Emails");
        GetCampaignsEndpoint.Map(group);
        GetCampaignEndpoint.Map(group);
        CreateCampaignEndpoint.Map(group);
        UpdateCampaignEndpoint.Map(group);
        DeleteCampaignEndpoint.Map(group);
        CopyCampaignEndpoint.Map(group);
        AddAttachmentEndpoint.Map(group);
        RemoveAttachmentEndpoint.Map(group);
        PreviewCampaignEndpoint.Map(group);
        GetPlanEndpoint.Map(group);
        SendTestEndpoint.Map(group);
        RunCampaignEndpoint.Map(group);
        PauseCampaignEndpoint.Map(group);
        RetryFailedEndpoint.Map(group);
        GetMessagesEndpoint.Map(group);
        MarkMessageEndpoint.Map(group);
        ExportLogEndpoint.Map(group);
    }
}
