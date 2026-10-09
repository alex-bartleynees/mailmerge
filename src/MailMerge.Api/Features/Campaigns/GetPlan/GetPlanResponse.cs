using MailMerge.Api.Features.Campaigns.Delivery;

namespace MailMerge.Api.Features.Campaigns.GetPlan;

public sealed record GetPlanResponse(DeliveryPlan Plan, bool SupportsDrafts, string? Account, string? FromEmail);
