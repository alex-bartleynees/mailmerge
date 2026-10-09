namespace MailMerge.Api.Features.Privacy.GetLog;

public sealed record PrivacyLogView(string Action, string PerformedBy, DateTimeOffset PerformedAt, IReadOnlyDictionary<string, int> Counts);
