using System.ComponentModel.DataAnnotations;

namespace MailMerge.Api.Host;

public sealed class AppSettings
{
    public const string SectionName = "App";

    /// <summary>
    /// The browser-facing origin (the BFF), e.g. <c>https://mailmerge.example.com</c>. Used for links that leave the
    /// app: unsubscribe links in emails and the Microsoft sign-in redirect.
    /// </summary>
    [Required]
    [Url]
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>Largest spreadsheet or attachment accepted, in megabytes.</summary>
    [Range(1, 150)]
    public int MaxUploadMegabytes { get; set; } = 25;

    public long MaxUploadBytes => MaxUploadMegabytes * 1024L * 1024L;
}
