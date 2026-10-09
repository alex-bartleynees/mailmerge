using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClosedXML.Excel;

namespace MailMerge.IntegrationTests.Infrastructure;

public static class TestData
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static byte[] Xlsx(string sheet, params object?[][] rows)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.AddWorksheet(sheet);
        for (var r = 0; r < rows.Length; r++)
        {
            for (var c = 0; c < rows[r].Length; c++)
            {
                ws.Cell(r + 1, c + 1).Value = rows[r][c] switch
                {
                    null => Blank.Value,
                    int i => i,
                    var v => v.ToString(),
                };
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static MultipartFormDataContent File(string fileName, byte[] content, string field = "file")
    {
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { part, field, fileName } };
    }

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"{(int)response.StatusCode} {response.ReasonPhrase}: {await response.Content.ReadAsStringAsync()}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    public static async Task<T> GetAsync<T>(this HttpClient client, string url) =>
        await (await client.GetAsync(url)).ReadAsync<T>();

    public static async Task<T> PostAsync<T>(this HttpClient client, string url, object body) =>
        await (await client.PostAsJsonAsync(url, body, Json)).ReadAsync<T>();

    public static async Task<T> PutAsync<T>(this HttpClient client, string url, object body) =>
        await (await client.PutAsJsonAsync(url, body, Json)).ReadAsync<T>();

    public static async Task<string> ProblemDetailAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("detail").GetString() ?? "";
    }
}

public sealed record ListDto(long Id, string Name, List<string> Columns, int Count);

public sealed record ContactDto(long Id, string Email, bool DoNotEmail, Dictionary<string, string> Data, bool Valid, bool OptedOut);

public sealed record ListContactsDto(long Id, string Name, List<string> Columns, List<ContactDto> Contacts);

public sealed record ImportPreviewDto(
    Guid Token, string FileName, List<string> Sheets, string Sheet, List<string> Headers, string EmailColumn,
    int RowCount, List<List<string>> Sample, int InvalidCount, int BlankCount);

public sealed record CommitImportDto(long ListId, int Added, int Updated, int Duplicates, int Invalid, int NoEmail);

public sealed record OptOutDto(string Id, string? Email, DateTimeOffset Added, string? Reason, string Source);

public sealed record AttachmentDto(long Id, string FileName, string ContentType, long Size, bool Available);

public sealed record CampaignDto(
    long Id, string Name, long? AccountId, long? ListId, string Subject, string Body, bool IsHtml, string Mode,
    string Status, string? Note, bool UnsubscribeFooter, DateTimeOffset? ArchivedAt, DateTimeOffset? PurgesOn,
    Dictionary<string, int> Counts, List<AttachmentDto> Attachments);

public sealed record PreviewDto(
    string To, string Subject, string Body, bool IsHtml, long? ContactId, long? PreviousId, long? NextId, int Total,
    List<string> UnknownFields, List<string> EmptyFields);

public sealed record PlanDto(int Contacts, int Skipped, int ToDraft, int ToSend, int AlreadySent, int Failed, int Uncertain);

public sealed record PlanResponseDto(PlanDto Plan, bool SupportsDrafts, string? Account, string? FromEmail);
