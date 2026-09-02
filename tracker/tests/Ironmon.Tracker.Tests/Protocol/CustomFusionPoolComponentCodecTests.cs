namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies the compact custom-fusion eligibility component shared with the game runtime.
/// </summary>
public sealed class CustomFusionPoolComponentCodecTests
{
    private const string FixtureFileName = "custom-fusion-pool-v1.json";
    private const string NormalSpeciesCountPropertyName = "normal_species_count";
    private const string IdentitiesPropertyName = "identities";
    private const string EncodedBase64PropertyName = "encoded_base64";
    private const char BodyPrefix = 'B';
    private const char HeadPrefix = 'H';

    /// <summary>
    /// Verifies C# produces and consumes the exact shared Ruby binary vector.
    /// </summary>
    [Fact]
    public void SharedGoldenVectorRoundTripsExactPackedBytes()
    {
        using JsonDocument fixture = LoadFixture();
        int normalSpeciesCount = fixture.RootElement.GetProperty(NormalSpeciesCountPropertyName).GetInt32();
        CustomFusionPair[] pairs = [.. fixture.RootElement.GetProperty(IdentitiesPropertyName).EnumerateArray().Select(ParsePair)];
        byte[] expected = Convert.FromBase64String(fixture.RootElement.GetProperty(EncodedBase64PropertyName).GetString()!);

        byte[] encoded = CustomFusionPoolComponentCodec.Encode(normalSpeciesCount, pairs);
        CustomFusionPoolComponent decoded = CustomFusionPoolComponentCodec.Decode(encoded);

        Assert.Equal(expected, encoded);
        Assert.Equal(CustomFusionPoolComponentCodec.SchemaVersion, decoded.SchemaVersion);
        Assert.Equal(normalSpeciesCount, decoded.NormalSpeciesCount);
        Assert.Equal(pairs.Length, decoded.EligibleCount);
        Assert.Equal(pairs.Select(FormatPair), decoded.Enumerate().Select(FormatPair));
        Assert.True(decoded.Contains(4, 1));
        Assert.False(decoded.Contains(1, 1));
    }

    /// <summary>
    /// Verifies corrupted membership counts are rejected instead of partially decoded.
    /// </summary>
    [Fact]
    public void CorruptedEligibilityCountIsRejected()
    {
        using JsonDocument fixture = LoadFixture();
        byte[] encoded = Convert.FromBase64String(fixture.RootElement.GetProperty(EncodedBase64PropertyName).GetString()!);
        encoded[^1] = 0;

        Assert.Throws<ArgumentException>(() => CustomFusionPoolComponentCodec.Decode(encoded));
    }

    /// <summary>
    /// Parses one compact fusion identity from the shared fixture.
    /// </summary>
    /// <param name="element">The identity JSON value.</param>
    /// <returns>The parsed material pair.</returns>
    private static CustomFusionPair ParsePair(JsonElement element)
    {
        string identity = element.GetString()!;
        int separator = identity.IndexOf(HeadPrefix);
        int bodyId = int.Parse(identity.AsSpan(1, separator - 1));
        int headId = int.Parse(identity.AsSpan(separator + 1));
        return new CustomFusionPair(bodyId, headId);
    }

    /// <summary>
    /// Formats one material pair as a compact fusion identity.
    /// </summary>
    /// <param name="pair">The pair to format.</param>
    /// <returns>The compact fusion identity.</returns>
    private static string FormatPair(CustomFusionPair pair)
    {
        return $"{BodyPrefix}{pair.BodyId}{HeadPrefix}{pair.HeadId}";
    }

    /// <summary>
    /// Loads the shared cross-runtime packed-component fixture.
    /// </summary>
    /// <returns>The parsed fixture document.</returns>
    private static JsonDocument LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixtureFileName);
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
}
