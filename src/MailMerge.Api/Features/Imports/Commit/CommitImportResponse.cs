namespace MailMerge.Api.Features.Imports.Commit;

/// <summary>
/// <c>Invalid</c> counts added rows whose address looks wrong (kept so staff can fix them);
/// <c>Duplicates</c> are rows whose address was already in the list (or earlier in the file).
/// </summary>
public sealed record CommitImportResponse(long ListId, int Added, int Updated, int Duplicates, int Invalid, int NoEmail);
