using System.Text.Json;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Persists explicit test-update selection without changing release signing trust or installation authority.
/// </summary>
public sealed partial class ReleaseDiscovery
{
    private const string TestCacheName = "discovery-test.json";
    private const string PreferencesName = "discovery-preferences.json";
    private const int MaximumReleaseCount = 100;
    private readonly string _testCachePath;
    private readonly string _preferencesPath;

    /// <summary>
    /// Gets the persistent explicit opt-in to published test releases; missing or unreadable preferences default to stable only.
    /// </summary>
    public bool IncludeTestReleases { get; private set; }

    /// <summary>
    /// Gets the channel-specific authenticated cache without sharing conditional headers or rollback watermarks.
    /// </summary>
    private string CachePath => IncludeTestReleases ? _testCachePath : _cachePath;

    /// <summary>
    /// Persists an explicit channel change and invalidates the completed observation before another release check.
    /// </summary>
    /// <param name="includeTestReleases">Whether published prereleases may be offered alongside stable releases.</param>
    /// <param name="cancellationToken">The caller's cancellation token while waiting for an active check.</param>
    /// <returns>The completed durable preference change; a failed write leaves the active channel unchanged.</returns>
    public async Task SetIncludeTestReleasesAsync(bool includeTestReleases, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IncludeTestReleases == includeTestReleases)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
            TransactionStorage.WriteDurable(_preferencesPath, ReleaseJson.Serialize(new DiscoveryPreferences(1, includeTestReleases)));
            IncludeTestReleases = includeTestReleases;
            _startupObservation = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Loads the shared user preference without letting optional malformed settings opt a player into testing.
    /// </summary>
    /// <returns>The valid stored opt-in, or false when it is missing, unsupported, or unreadable.</returns>
    private bool ReadPreferences()
    {
        try
        {
            if (!File.Exists(_preferencesPath))
                return false;

            var preferences = ReleaseJson.Parse<DiscoveryPreferences>(TransactionStorage.ReadBytes(_preferencesPath), ReleaseJson.ManifestLimit);
            return preferences.SchemaVersion == 1 && preferences.IncludeTestReleases;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Selects the highest numeric published version while keeping drafts, unrelated tags, and unsigned payloads ineligible.
    /// </summary>
    /// <param name="bytes">The fixed repository's bounded latest response or published release list.</param>
    /// <returns>The exact candidate version and whether it is a test release; its manifest still requires independent verification.</returns>
    private (string Version, bool IsTestRelease) ReadDiscoveryIdentity(byte[] bytes)
    {
        if (!IncludeTestReleases)
            return (ValidateApi(bytes), false);

        ReleaseJson.Validate(bytes, ReleaseJson.ManifestLimit);
        using var document = JsonDocument.Parse(bytes);
        var releases = document.RootElement;
        if (releases.ValueKind != JsonValueKind.Array || releases.GetArrayLength() > MaximumReleaseCount)
            throw new InvalidDataException(UpdaterText.SetupVersionsUnavailable);

        JsonElement? selected = null;
        Version? highest = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty(DraftProperty, out var draft) || draft.ValueKind != JsonValueKind.False
                || !release.TryGetProperty(PrereleaseProperty, out var prerelease) || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !release.TryGetProperty(TagProperty, out var tag) || tag.ValueKind != JsonValueKind.String || tag.GetString() is not { } name || !name.StartsWith(TagPrefix, StringComparison.Ordinal))
                continue;

            Version version;
            try
            {
                version = ReleaseProtocol.ParseVersion(name[TagPrefix.Length..]);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (highest is null || version > highest)
            {
                highest = version;
                selected = release;
            }
        }

        if (selected is not { } candidate)
            throw new InvalidDataException(UpdaterText.SetupVersionsUnavailable);

        return (ValidateApi(JsonSerializer.SerializeToUtf8Bytes(candidate), allowTestRelease: true), candidate.GetProperty(PrereleaseProperty).GetBoolean());
    }

    /// <summary>
    /// Stores only the explicit release-channel choice outside the installation so it survives tracker replacement.
    /// </summary>
    /// <remarks>
    /// Constructs a versioned preference record; it carries no signing trust, download endpoint, or installation authority.
    /// </remarks>
    /// <param name="SchemaVersion">The supported preference format.</param>
    /// <param name="IncludeTestReleases">Whether the user explicitly opted into published prereleases.</param>
    private sealed record DiscoveryPreferences(int SchemaVersion, bool IncludeTestReleases);
}
