namespace MailMerge.Api.Features.Campaigns.Messages;

public sealed record MessageView(long Id, string Email, MessageStatus Status, string? Error, DateTimeOffset Updated);
