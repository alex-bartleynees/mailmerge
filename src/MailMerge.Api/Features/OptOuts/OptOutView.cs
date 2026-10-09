namespace MailMerge.Api.Features.OptOuts;

/// <summary><c>Email</c> is null when the person was erased for privacy; <c>Id</c> still identifies the entry.</summary>
public sealed record OptOutView(string Id, string? Email, DateTimeOffset Added, string? Reason, OptOutSource Source)
{
    public static OptOutView From(OptOut o) => new(o.EmailHash, o.Email, o.Added, o.Reason, o.Source);
}
