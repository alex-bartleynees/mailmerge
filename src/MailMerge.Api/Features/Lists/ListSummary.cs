namespace MailMerge.Api.Features.Lists;

/// <summary>A contact list as shown in pickers and the Contacts tab.</summary>
public sealed record ListSummary(long Id, string Name, IReadOnlyList<string> Columns, int Count, DateTimeOffset Created);
