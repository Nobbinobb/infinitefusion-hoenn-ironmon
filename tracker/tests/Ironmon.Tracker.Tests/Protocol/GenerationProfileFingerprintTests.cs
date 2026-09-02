using System.Text;

namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies the immutable generation-profile identity contract shared with the game runtime.
/// </summary>
public sealed class GenerationProfileFingerprintTests
{
    private const string FixtureFileName = "generation-profile-v1.json";
    private const string ManifestPropertyName = "manifest";
    private const string CanonicalJsonPropertyName = "canonical_json";
    private const string ProfileIdPropertyName = "profile_id";

    /// <summary>
    /// Verifies the tracker and bundled runtime share one canonical JSON and SHA-256 golden vector.
    /// </summary>
    [Fact]
    public void SharedGoldenVectorProducesExpectedCanonicalJsonAndProfileId()
    {
        using JsonDocument fixture = LoadFixture();
        JsonElement manifestElement = fixture.RootElement.GetProperty(ManifestPropertyName);
        GenerationProfilePayload profile = TrackerJson.DeserializePayload<GenerationProfilePayload>(manifestElement);

        string canonicalJson = Encoding.UTF8.GetString(GenerationProfileFingerprint.CreateCanonicalJson(profile));
        Assert.Equal(fixture.RootElement.GetProperty(CanonicalJsonPropertyName).GetString(), canonicalJson);
        Assert.Equal(fixture.RootElement.GetProperty(ProfileIdPropertyName).GetString(), GenerationProfileFingerprint.Create(profile));
    }

    /// <summary>
    /// Verifies descriptor ordering is normalized before identity is calculated.
    /// </summary>
    [Fact]
    public void DescriptorInputOrderDoesNotAffectProfileId()
    {
        using JsonDocument fixture = LoadFixture();
        GenerationProfilePayload profile = TrackerJson.DeserializePayload<GenerationProfilePayload>(fixture.RootElement.GetProperty(ManifestPropertyName));
        GenerationProfilePayload reordered = new()
        {
            SchemaVersion = profile.SchemaVersion,
            Algorithms = [.. profile.Algorithms.Reverse()],
            Components = [.. profile.Components.Reverse()]
        };

        Assert.Equal(GenerationProfileFingerprint.Create(profile), GenerationProfileFingerprint.Create(reordered));
    }

    /// <summary>
    /// Verifies incomplete algorithm sets cannot be mistaken for valid profiles.
    /// </summary>
    [Fact]
    public void MissingAlgorithmFamilyIsRejected()
    {
        using JsonDocument fixture = LoadFixture();
        GenerationProfilePayload profile = TrackerJson.DeserializePayload<GenerationProfilePayload>(fixture.RootElement.GetProperty(ManifestPropertyName));
        GenerationProfilePayload incomplete = new()
        {
            SchemaVersion = profile.SchemaVersion,
            Algorithms = [.. profile.Algorithms.Skip(1)],
            Components = profile.Components
        };

        Assert.Throws<ArgumentException>(() => GenerationProfileFingerprint.Create(incomplete));
    }

    /// <summary>
    /// Loads the shared cross-runtime golden fixture.
    /// </summary>
    /// <returns>The parsed fixture document.</returns>
    private static JsonDocument LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixtureFileName);
        return JsonDocument.Parse(File.ReadAllBytes(path));
    }
}
