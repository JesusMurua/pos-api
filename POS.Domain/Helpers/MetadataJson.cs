using System.Text.Json;

namespace POS.Domain.Helpers;

/// <summary>
/// Boundary helper for translating between the wire-format raw JSON strings
/// (used by the offline POS sync DTOs and admin payment requests) and the
/// strongly-typed metadata classes persisted via EF Core 9 owned-type JSON.
/// Intentionally lenient on read: malformed JSON returns null instead of
/// throwing so a single bad payload cannot fail an entire sync batch.
/// </summary>
public static class MetadataJson
{
    /// <summary>
    /// Web defaults give us camelCase property naming and case-insensitive
    /// matching on read — the same behavior the API layer already uses.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Deserializes a raw JSON string into <typeparamref name="T"/>. Returns
    /// null when the input is null, blank, or not parseable.
    /// </summary>
    public static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Serializes <typeparamref name="T"/> back to a raw JSON string for wire
    /// formats that still expose <c>string?</c> (e.g. <c>OrderPullItemDto</c>).
    /// Returns null when the value is null.
    /// </summary>
    public static string? Serialize<T>(T? value) where T : class
        => value is null ? null : JsonSerializer.Serialize(value, Options);
}
