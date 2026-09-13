using Ironmon.Updater.Core;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Parses bounded, unambiguous UTF-8 release documents with a cached strict wire contract.
/// </summary>
internal static class ReleaseJson
{
    internal const int ManifestLimit = 1024 * 1024;
    internal const int SignatureLimit = 16384;
    internal const int InventoryLimit = 32 * 1024 * 1024;
    private static readonly UTF8Encoding _utf8 = new(false, true);
    private static readonly JsonSerializerOptions _options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true, MaxDepth = 32 };

    /// <summary>
    /// Serializes local evidence with one cached options instance.
    /// </summary>
    /// <typeparam name="T">The closed document type.</typeparam>
    /// <param name="value">The document.</param>
    /// <returns>The UTF-8 JSON bytes.</returns>
    internal static byte[] Serialize<T>(T value)
        => JsonSerializer.SerializeToUtf8Bytes(value, _options);

    /// <summary>
    /// Rejects BOM, invalid encoding, duplicate keys, unknown fields and missing required values.
    /// </summary>
    /// <typeparam name="T">The expected wire type.</typeparam>
    /// <param name="bytes">The exact document bytes.</param>
    /// <param name="limit">The maximum permitted size.</param>
    /// <returns>The strictly parsed document.</returns>
    internal static T Parse<T>(byte[] bytes, int limit)
    {
        Validate(bytes, limit);
        return JsonSerializer.Deserialize<T>(bytes, _options) ?? throw new InvalidDataException(UpdaterText.ReleaseJsonTheReleaseDocumentIsEmpty);
    }

    /// <summary>
    /// Validates raw JSON before any permissive external API projection.
    /// </summary>
    /// <param name="bytes">The document bytes.</param>
    /// <param name="limit">The maximum permitted size.</param>
    internal static void Validate(byte[] bytes, int limit)
    {
        if (bytes.Length == 0 || bytes.Length > limit || bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            throw new InvalidDataException(UpdaterText.ReleaseJsonTheReleaseDocumentIsEmptyOversizedOrHasAn);

        _utf8.GetCharCount(bytes);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        ValidateElement(document.RootElement);
    }

    /// <summary>
    /// Rejects duplicate object members at every level of a release document.
    /// </summary>
    /// <param name="element">The current JSON value.</param>
    private static void ValidateElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in element.EnumerateObject())
            {
                if (!names.Add(member.Name))
                    throw new InvalidDataException(UpdaterText.ReleaseJsonDuplicateReleaseDocumentPropertiesAreNotAllowed);

                ValidateElement(member.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                if (child.ValueKind == JsonValueKind.Null)
                    throw new InvalidDataException(UpdaterText.ReleaseJsonReleaseDocumentArraysMustNotContainNullEntries);

                ValidateElement(child);
            }
        }
    }
}
