using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailMerge.Api.Shared.Persistence;

internal static class JsonColumnExtensions
{
    // Enums as camelCase text so stored values survive enum reordering and read naturally in SQL.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Stores the property as <c>jsonb</c> via System.Text.Json, with value comparison so edits are detected.</summary>
    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> property)
    {
        property
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                s => JsonSerializer.Deserialize<T>(s, Json)!,
                new ValueComparer<T>(
                    (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(v, Json), Json)!))
            .HasColumnType("jsonb");
        return property;
    }
}
