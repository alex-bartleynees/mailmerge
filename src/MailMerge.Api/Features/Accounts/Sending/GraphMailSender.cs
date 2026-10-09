using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MailMerge.Api.Features.Accounts.Microsoft365;

namespace MailMerge.Api.Features.Accounts.Sending;

/// <summary>
/// Microsoft 365 via Graph, acting as the signed-in user on their own mailbox. Drafts land in Outlook's Drafts
/// folder; sent drafts move to Sent Items.
/// <para>
/// Retry rules: 429 is retried everywhere (the request wasn't processed) honouring <c>Retry-After</c>. 503/504 are
/// retried for everything except <c>/send</c>: a gateway error there may hide a successful send, so the message is
/// reported as uncertain instead of risking a duplicate.
/// </para>
/// </summary>
public sealed class GraphMailSender(
    SendingAccount account,
    HttpClient http,
    IMicrosoftTokenProvider tokens,
    MicrosoftSettings settings,
    TimeProvider clock,
    ILogger<GraphMailSender> log) : IMailSender
{
    public const string HttpClientName = "graph";

    /// <summary>Graph's limit for attaching in one request; larger files use an upload session.</summary>
    internal const int InlineAttachmentMax = 3_000_000;

    /// <summary>Upload-session chunks must be a multiple of 320 KiB.</summary>
    internal const int ChunkSize = 4 * 327_680;

    private const int MaxAttempts = 6;

    public bool SupportsDrafts => true;

    public async Task<string> CreateDraftAsync(OutgoingMessage message, CancellationToken ct)
    {
        var payload = new
        {
            subject = message.Subject,
            body = new { contentType = message.IsHtml ? "HTML" : "Text", content = message.Body },
            toRecipients = new[] { new { emailAddress = new { address = message.To } } },
        };
        using var created = await CallAsync(HttpMethod.Post, "/me/messages", () => JsonContent.Create(payload), isSend: false, ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("id").GetString()!;

        try
        {
            foreach (var attachment in message.Attachments)
            {
                await AttachAsync(id, attachment, ct);
            }
        }
        catch
        {
            // Don't leave a half-built draft in the shop's Drafts folder.
            try
            {
                using var _ = await CallAsync(HttpMethod.Delete, $"/me/messages/{Uri.EscapeDataString(id)}", null, isSend: false, CancellationToken.None);
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Couldn't delete incomplete draft for account {AccountId}", account.Id);
            }

            throw;
        }

        return id;
    }

    public async Task SendDraftAsync(string draftId, CancellationToken ct)
    {
        using var _ = await CallAsync(HttpMethod.Post, $"/me/messages/{Uri.EscapeDataString(draftId)}/send", null, isSend: true, ct);
    }

    public async Task SendNowAsync(OutgoingMessage message, CancellationToken ct) =>
        await SendDraftAsync(await CreateDraftAsync(message, ct), ct);

    public async Task<string> CheckAsync(CancellationToken ct)
    {
        using var response = await CallAsync(HttpMethod.Get, "/me?$select=mail,userPrincipalName,displayName", null, isSend: false, ct);
        var me = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var mailbox = me.TryGetProperty("mail", out var mail) && mail.GetString() is { Length: > 0 } m
            ? m
            : me.GetProperty("userPrincipalName").GetString();
        return $"Connected as {mailbox}";
    }

    private async Task AttachAsync(string messageId, OutgoingAttachment attachment, CancellationToken ct)
    {
        var path = $"/me/messages/{Uri.EscapeDataString(messageId)}/attachments";
        if (attachment.Content.Length <= InlineAttachmentMax)
        {
            var inline = new Dictionary<string, object>
            {
                ["@odata.type"] = "#microsoft.graph.fileAttachment",
                ["name"] = attachment.FileName,
                ["contentType"] = attachment.ContentType,
                ["contentBytes"] = Convert.ToBase64String(attachment.Content),
            };
            using var _ = await CallAsync(HttpMethod.Post, path, () => JsonContent.Create(inline), isSend: false, ct);
            return;
        }

        var session = new
        {
            AttachmentItem = new
            {
                attachmentType = "file",
                name = attachment.FileName,
                size = attachment.Content.Length,
                contentType = attachment.ContentType,
            },
        };
        using var created = await CallAsync(HttpMethod.Post, path + "/createUploadSession", () => JsonContent.Create(session), isSend: false, ct);
        var uploadUrl = (await created.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("uploadUrl").GetString()!;

        var total = attachment.Content.Length;
        for (var offset = 0; offset < total; offset += ChunkSize)
        {
            var length = Math.Min(ChunkSize, total - offset);
            // The upload URL is pre-authorised; sending a bearer token to it is rejected.
            using var put = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(attachment.Content, offset, length),
            };
            put.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + length - 1, total);
            using var response = await http.SendAsync(put, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new MessageRejectedException(
                    $"Attachment upload failed ({(int)response.StatusCode}): {await ErrorTextAsync(response, ct)}");
            }
        }
    }

    private async Task<HttpResponseMessage> CallAsync(
        HttpMethod method, string path, Func<HttpContent>? content, bool isSend, CancellationToken ct)
    {
        var url = settings.GraphBaseUrl.TrimEnd('/') + path;
        for (var attempt = 1; ; attempt++)
        {
            var token = await tokens.GetAccessTokenAsync(account, ct);
            using var request = new HttpRequestMessage(method, url) { Content = content?.Invoke() };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request, ct);
            }
            catch (Exception e) when (isSend && !ct.IsCancellationRequested && e is HttpRequestException or TaskCanceledException)
            {
                throw new DeliveryUncertainException(
                    $"Lost contact with Microsoft while sending ({e.Message}). Check Sent Items.", e);
            }

            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            using (response)
            {
                var status = response.StatusCode;
                var retryable = status == HttpStatusCode.TooManyRequests
                                || (!isSend && status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout);
                if (retryable && attempt < MaxAttempts)
                {
                    var wait = RetryDelay(response, attempt);
                    log.LogInformation("Graph {Status} for account {AccountId}; retrying in {Delay}", (int)status, account.Id, wait);
                    await Task.Delay(wait, clock, ct);
                    continue;
                }

                var error = await ErrorTextAsync(response, ct);
                if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new AccountProblemException(
                        $"Microsoft refused the request ({(int)status}). The shop's Microsoft admin may need to approve " +
                        $"the app, or you may need to sign in again. {error}".TrimEnd());
                }

                if (status == HttpStatusCode.TooManyRequests)
                {
                    throw new MessageRejectedException("Microsoft kept asking us to slow down. Try again later.");
                }

                if (isSend && ((int)status >= 500 || status == HttpStatusCode.NotFound))
                {
                    // 404: the draft is gone, most likely because it was already sent (or deleted in Outlook).
                    throw new DeliveryUncertainException(
                        $"Microsoft didn't confirm this was sent ({(int)status}). Check Sent Items. {error}".TrimEnd());
                }

                throw new MessageRejectedException($"Microsoft Graph {(int)status}: {error}".TrimEnd());
            }
        }
    }

    private static TimeSpan RetryDelay(HttpResponseMessage response, int attempt)
    {
        var header = response.Headers.RetryAfter;
        var delay = header?.Delta
                    ?? (header?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(5 * attempt));
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay > TimeSpan.FromMinutes(2) ? TimeSpan.FromMinutes(2) : delay;
    }

    private static async Task<string> ErrorTextAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
            {
                text = message.GetString() ?? "";
            }
        }
        catch (JsonException)
        {
            // Not JSON; use the raw text.
        }

        return text.Length > 300 ? text[..300] : text;
    }
}
