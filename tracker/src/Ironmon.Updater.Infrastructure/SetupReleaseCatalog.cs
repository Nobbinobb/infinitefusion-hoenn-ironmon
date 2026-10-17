using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Reauthenticates explicit installation and update targets independently of automatic latest-release rollback protection.
/// </summary>
/// <remarks>
/// Constructs a catalog restricted to the same public Ironmon and upstream game repositories.
/// </remarks>
/// <param name="http">The bounded credential-free release transport.</param>
/// <param name="verifier">The independently configured release trust.</param>
public sealed class SetupReleaseCatalog(ReleaseHttpClient http, ReleaseVerifier verifier)
{
    private const int _catalogLimit = 2 * 1024 * 1024;
    private const int _maximumReleases = 100;
    private const string _tagProperty = "tag_name";
    private const string _draftProperty = "draft";
    private const string _prereleaseProperty = "prerelease";
    private const string _tagPrefix = "v";
    private const string _statusProperty = "status";
    private const string _baseProperty = "base_commit";
    private const string _mergeBaseProperty = "merge_base_commit";
    private const string _shaProperty = "sha";
    private const string _ahead = "ahead";
    private const string _identical = "identical";

    /// <summary>
    /// Lists recent stable releases, disabling each target whose signed content or required game cannot be verified now.
    /// </summary>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>At most one hundred choices ordered by numeric Ironmon version.</returns>
    public async Task<SetupReleaseChoice[]> ListAsync(CancellationToken cancellationToken)
    {
        var response = await http.ReadAsync(new Uri(ReleaseProtocol.ReleaseListUrl), _catalogLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        ReleaseJson.Validate(response.Bytes, _catalogLimit);
        using var document = JsonDocument.Parse(response.Bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() > _maximumReleases)
            throw new InvalidDataException(UpdaterText.SetupVersionsUnavailable);

        var releases = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(_tagProperty, out var tag) || tag.ValueKind != JsonValueKind.String || !item.TryGetProperty(_draftProperty, out var draft) || draft.ValueKind != JsonValueKind.False || !item.TryGetProperty(_prereleaseProperty, out var prerelease) || prerelease.ValueKind != JsonValueKind.False)
                continue;

            var text = tag.GetString()!;
            if (!text.StartsWith(_tagPrefix, StringComparison.Ordinal))
                continue;

            var version = text[_tagPrefix.Length..];
            try
            {
                ReleaseProtocol.ParseVersion(version);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!releases.TryAdd(version, item.Clone()))
                throw new InvalidDataException(UpdaterText.SetupVersionsUnavailable);
        }

        KeyValuePair<string, JsonElement>[] entries = [.. releases];
        var choices = new SetupReleaseChoice[entries.Length];
        var games = new ConcurrentDictionary<string, Lazy<Task<bool>>>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(Enumerable.Range(0, entries.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (index, token) =>
        {
            var entry = entries[index];
            ReleaseManifest? manifest = null;
            try
            {
                var evidence = await ReadVerifiedAsync(entry.Key, entry.Value, token).ConfigureAwait(false);
                manifest = verifier.Verify(evidence.Manifest, evidence.Signatures);
                await RequireAvailableAsync(manifest, games, token).ConfigureAwait(false);
                choices[index] = new SetupReleaseChoice(entry.Key, manifest.Game.VersionLabel, manifest.Game.PreferredCommit, null);
            }
            catch (Exception error) when (!token.IsCancellationRequested && IsUnavailable(error))
            {
                choices[index] = new SetupReleaseChoice(entry.Key, manifest?.Game.VersionLabel, manifest?.Game.PreferredCommit, error is InvalidDataException or InvalidOperationException ? error.Message : UpdaterText.SetupVersionsUnavailable);
            }
        }).ConfigureAwait(false);
        return [.. choices.OrderByDescending(choice => ReleaseProtocol.ParseVersion(choice.Version))];
    }

    /// <summary>
    /// Rechecks one exact published target, its signed metadata, downloads and current upstream game availability.
    /// </summary>
    /// <param name="version">The explicitly chosen stable Ironmon version.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>Fresh authenticated evidence; unavailable targets never fall back to latest.</returns>
    public async Task<ReleaseEvidence> ReadAsync(string version, CancellationToken cancellationToken)
    {
        ReleaseProtocol.ParseVersion(version);
        var response = await http.ReadAsync(new Uri(ReleaseProtocol.ReleaseTagUrl + version), ReleaseJson.ManifestLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(response.Bytes);
        var evidence = await ReadVerifiedAsync(version, document.RootElement, cancellationToken).ConfigureAwait(false);
        var manifest = verifier.Verify(evidence.Manifest, evidence.Signatures);
        await RequireAvailableAsync(manifest, new ConcurrentDictionary<string, Lazy<Task<bool>>>(StringComparer.Ordinal), cancellationToken).ConfigureAwait(false);
        return evidence;
    }

    /// <summary>
    /// Validates the public API identity before reading and independently verifying the exact release manifest.
    /// </summary>
    /// <param name="version">The selected stable version.</param>
    /// <param name="api">The public release API entry.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The signed release evidence without program downloads.</returns>
    private async Task<ReleaseEvidence> ReadVerifiedAsync(string version, JsonElement api, CancellationToken cancellationToken)
    {
        try
        {
            if (ReleaseDiscovery.ValidateApi(Encoding.UTF8.GetBytes(api.GetRawText())) != version)
                throw new InvalidDataException(UpdaterText.SetupVersionsMissingMetadata);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException(UpdaterText.SetupVersionsMissingMetadata, error);
        }

        var manifest = await http.ReadAsync(new Uri(ReleaseProtocol.AssetUrl(version, ReleaseProtocol.ManifestName)), ReleaseJson.ManifestLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        var signature = await http.ReadAsync(new Uri(ReleaseProtocol.AssetUrl(version, ReleaseProtocol.SignatureName)), ReleaseJson.SignatureLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            verifier.Verify(manifest.Bytes, signature.Bytes, _tagPrefix + version);
        }
        catch (Exception error) when (error is InvalidDataException or JsonException or CryptographicException or FormatException)
        {
            throw new InvalidDataException(UpdaterText.SetupVersionsMissingMetadata, error);
        }

        return new ReleaseEvidence(manifest.Bytes, signature.Bytes, []);
    }

    /// <summary>
    /// Requires a supported engine, reachable game ancestry/archive and every signed installation download.
    /// </summary>
    /// <param name="manifest">The independently authenticated release.</param>
    /// <param name="games">Game observations shared only within this current catalog request.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The completed current availability check.</returns>
    private async Task RequireAvailableAsync(ReleaseManifest manifest, ConcurrentDictionary<string, Lazy<Task<bool>>> games, CancellationToken cancellationToken)
    {
        if (ReleaseProtocol.ParseVersion(manifest.MinimumEngineVersion) > ReleaseProtocol.ParseVersion(ReleaseProtocol.EngineVersion))
            throw new InvalidOperationException(UpdaterText.SetupVersionsNewerSetupRequired);

        var game = games.GetOrAdd(manifest.Game.PreferredCommit, commit => new Lazy<Task<bool>>(() => GameAvailableAsync(commit, cancellationToken)));
        if (!await game.Value.ConfigureAwait(false))
            throw new InvalidDataException(UpdaterText.SetupVersionsGameUnavailable);

        var files = manifest.Assets.Where(asset => asset.Role != ReleaseProtocol.SetupRole).Select(asset => (asset.Url, Bytes: asset.Container?.Bytes ?? asset.Bytes)).Distinct();
        try
        {
            foreach (var file in files)
                if (!await http.IsAvailableAsync(new Uri(file.Url), file.Bytes, cancellationToken).ConfigureAwait(false))
                    throw new InvalidDataException(UpdaterText.SetupVersionsDownloadsUnavailable);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsUnavailable(error))
        {
            throw new InvalidDataException(UpdaterText.SetupVersionsDownloadsUnavailable, error);
        }
    }

    /// <summary>
    /// Checks exact upstream identity and forward ancestry, then requests archive headers without downloading the game.
    /// </summary>
    /// <param name="commit">The signed preferred game commit.</param>
    /// <param name="cancellationToken">The availability request token.</param>
    /// <returns>Whether this revision remains reachable in the supported upstream history and downloadable.</returns>
    private async Task<bool> GameAvailableAsync(string commit, CancellationToken cancellationToken)
    {
        try
        {
            var response = await http.ReadAsync(new Uri(ReleaseProtocol.GameCompareUrl + commit + ReleaseProtocol.GameCompareSuffix), ReleaseJson.ManifestLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
            ReleaseJson.Validate(response.Bytes, ReleaseJson.ManifestLimit);
            using var document = JsonDocument.Parse(response.Bytes);
            var root = document.RootElement;
            if (root.GetProperty(_statusProperty).GetString() is not (_ahead or _identical) || root.GetProperty(_baseProperty).GetProperty(_shaProperty).GetString() != commit || root.GetProperty(_mergeBaseProperty).GetProperty(_shaProperty).GetString() != commit)
                return false;

            return await http.IsAvailableAsync(new Uri(ReleaseProtocol.GameArchiveUrl + commit), null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested && IsUnavailable(error))
        {
            return false;
        }
    }

    /// <summary>
    /// Identifies bounded transport or metadata failures that disable a choice without hiding other releases.
    /// </summary>
    /// <param name="error">The observed release failure.</param>
    /// <returns>Whether another independently verified release can still be offered.</returns>
    private static bool IsUnavailable(Exception error)
        => error is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or JsonException or CryptographicException or FormatException or ArgumentException or KeyNotFoundException or OperationCanceledException;
}

/// <summary>
/// Describes one explicit installer target and its current availability without making unsigned data installation authority.
/// </summary>
/// <remarks>
/// Constructs a current availability observation; review and installation must check it again.
/// </remarks>
/// <param name="Version">The canonical Ironmon release version.</param>
/// <param name="GameVersion">The signed game display version, when its metadata can be authenticated.</param>
/// <param name="GameCommit">The exact signed preferred game commit, when known.</param>
/// <param name="UnavailableReason">The explanation for a disabled choice, or null when currently available.</param>
public sealed record SetupReleaseChoice(string Version, string? GameVersion, string? GameCommit, string? UnavailableReason)
{
    /// <summary>
    /// Gets whether this target passed the current bounded release and upstream availability checks.
    /// </summary>
    public bool CanInstall => UnavailableReason is null;
}
