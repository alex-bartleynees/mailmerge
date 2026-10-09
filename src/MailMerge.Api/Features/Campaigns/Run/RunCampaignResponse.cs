namespace MailMerge.Api.Features.Campaigns.Run;

public sealed record RunCampaignResponse(IReadOnlyDictionary<string, int> Counts);
