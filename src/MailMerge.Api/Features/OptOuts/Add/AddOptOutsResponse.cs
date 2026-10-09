namespace MailMerge.Api.Features.OptOuts.Add;

/// <summary><c>Ignored</c> lists entries that weren't email addresses.</summary>
public sealed record AddOptOutsResponse(int Added, int AlreadyOptedOut, IReadOnlyList<string> Ignored);
