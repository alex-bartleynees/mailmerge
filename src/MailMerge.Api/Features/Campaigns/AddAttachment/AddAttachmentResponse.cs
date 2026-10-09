namespace MailMerge.Api.Features.Campaigns.AddAttachment;

/// <summary><c>TotalBytes</c> lets the UI warn when the email is getting too big for recipients' mail servers.</summary>
public sealed record AddAttachmentResponse(AttachmentView Attachment, long TotalBytes);
