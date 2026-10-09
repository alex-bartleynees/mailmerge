namespace MailMerge.Api.Features.Campaigns.RetryFailed;

/// <summary><c>Requeued</c> rows go back to waiting; press Send or Create drafts to process them.</summary>
public sealed record RetryFailedResponse(int Requeued);
