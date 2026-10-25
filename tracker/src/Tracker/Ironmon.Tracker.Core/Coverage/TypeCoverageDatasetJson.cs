using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Reads and validates the release-precalculated type-coverage dataset.
/// </summary>
public static class TypeCoverageDatasetJson
{
    /// <summary>
    /// Gets the supported aggregate dataset schema version.
    /// </summary>
    public const int SchemaVersion = 1;

    private const int MaximumProfileCount = 171;
    private const int FingerprintLength = 16;
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>
    /// Deserializes and validates one aggregate dataset stream.
    /// </summary>
    /// <param name="stream">The UTF-8 JSON dataset stream.</param>
    /// <returns>The validated type-coverage dataset.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the stream is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the dataset violates its schema or population invariants.</exception>
    /// <exception cref="JsonException">Thrown when the JSON document cannot be deserialized.</exception>
    public static TypeCoverageDataset Deserialize(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        TypeCoverageDataset dataset = JsonSerializer.Deserialize<TypeCoverageDataset>(stream, _options) ?? throw new InvalidDataException("The type coverage dataset is empty.");
        Validate(dataset);
        return dataset;
    }

    /// <summary>
    /// Validates one deserialized aggregate dataset.
    /// </summary>
    /// <param name="dataset">The dataset to validate.</param>
    /// <exception cref="ArgumentNullException">Thrown when the dataset is null.</exception>
    /// <exception cref="InvalidDataException">Thrown when the dataset violates its schema or population invariants.</exception>
    public static void Validate(TypeCoverageDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.SchemaVersion != SchemaVersion)
            throw new InvalidDataException($"Type coverage dataset schema {dataset.SchemaVersion} is unsupported.");

        if (string.IsNullOrWhiteSpace(dataset.GameVersion))
            throw new InvalidDataException("The type coverage dataset game version is missing.");

        if (dataset.NormalPoolSize <= 0 || dataset.FusionPoolSize <= 0 || dataset.FusionPoolSchemaVersion <= 0)
            throw new InvalidDataException("Type coverage pool metadata must be positive.");

        ValidateFingerprint(dataset.NormalPoolFingerprint, "normal");
        ValidateFingerprint(dataset.FusionPoolFingerprint, "fusion");
        if (dataset.Profiles is null || dataset.Profiles.Count is <= 0 or > MaximumProfileCount)
            throw new InvalidDataException("The type coverage dataset profile count is invalid.");

        HashSet<string> keys = new(StringComparer.Ordinal);
        long normalTotal = 0;
        long fusionTotal = 0;
        foreach (TypeCoverageProfile profile in dataset.Profiles)
        {
            ValidateProfile(profile, keys);
            normalTotal += profile.NormalCount;
            fusionTotal += profile.FusionCount;
        }

        if (normalTotal != dataset.NormalPoolSize)
            throw new InvalidDataException($"Type coverage normal count {normalTotal} does not match pool size {dataset.NormalPoolSize}.");

        if (fusionTotal != dataset.FusionPoolSize)
            throw new InvalidDataException($"Type coverage fusion count {fusionTotal} does not match pool size {dataset.FusionPoolSize}.");
    }

    /// <summary>
    /// Validates one stable lowercase hexadecimal pool fingerprint.
    /// </summary>
    /// <param name="fingerprint">The fingerprint to validate.</param>
    /// <param name="poolName">The pool name used in failures.</param>
    /// <exception cref="InvalidDataException">Thrown when the fingerprint is malformed.</exception>
    private static void ValidateFingerprint(string fingerprint, string poolName)
    {
        if (fingerprint is null || fingerprint.Length != FingerprintLength || fingerprint.Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"The type coverage {poolName} pool fingerprint is malformed.");
    }

    /// <summary>
    /// Validates one occupied defensive profile and records its stable key.
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <param name="keys">Previously observed profile keys.</param>
    /// <exception cref="InvalidDataException">Thrown when the profile is malformed or duplicated.</exception>
    private static void ValidateProfile(TypeCoverageProfile profile, HashSet<string> keys)
    {
        if (profile is null || profile.Types is null || profile.Types.Count is <= 0 or > 2)
            throw new InvalidDataException("A type coverage profile must contain one or two types.");

        int previousOrder = -1;
        foreach (string type in profile.Types)
        {
            int order;
            try
            {
                order = PokemonTypeCatalog.GetOrder(type);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException("A type coverage profile contains an unsupported type.", exception);
            }

            if (order <= previousOrder)
                throw new InvalidDataException("Type coverage profile types must be unique and in stable chart order.");

            previousOrder = order;
        }

        if (profile.NormalCount < 0 || profile.FusionCount < 0 || profile.NormalCount == 0 && profile.FusionCount == 0)
            throw new InvalidDataException("Type coverage profile counts must describe an occupied population.");

        if (!keys.Add(string.Join('|', profile.Types)))
            throw new InvalidDataException("The type coverage dataset contains a duplicate profile.");
    }
}
