using System.Diagnostics;
using System.Text.Json;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Starts the independently published, authenticated updater for a reviewed release selection.
/// </summary>
public interface IStandaloneUpdater
{
    /// <summary>
    /// Authenticates and opens the worker without reviewing or mutating the selected installation.
    /// </summary>
    /// <param name="target">The installation and exact release selected by the player.</param>
    /// <param name="cancellationToken">The prelaunch cancellation token.</param>
    /// <returns>The completed launch.</returns>
    Task LaunchAsync(StandaloneUpdateTarget target, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches a versioned self-contained updater using a stable, separately signed worker contract.
/// </summary>
/// <remarks>
/// Uses the fixed public repository and a private cache shared by tracker installations for the same user.
/// </remarks>
/// <param name="http">The bounded public release transport.</param>
/// <param name="verifier">The embedded updater trust.</param>
/// <param name="downloads">The authenticated asset cache.</param>
public sealed class StandaloneUpdater(ReleaseHttpClient http, ReleaseVerifier verifier, ReleaseDownloadStore downloads) : IStandaloneUpdater
{
    internal const string TagPrefix = "vupdater-";
    internal const string DocumentType = "ironmon-standalone-updater";
    internal const string ManifestName = "updater-bootstrap.json";
    internal const string SignatureName = "updater-bootstrap.sig.json";
    internal const string ExecutableName = "Ironmon-Standalone-Updater-win-x64.exe";
    private const string WorkerRole = "standalone-updater";
    private const string CacheName = "standalone";
    private const string Watermark = "highest.json";
    private const string LockName = "selection.lock";
    private const int MetadataLimit = 16 * 1024;
    private const int FeedLimit = 2 * 1024 * 1024;
    private const string Draft = "draft";
    private const string Prerelease = "prerelease";
    private const string Tag = "tag_name";
    private const string PartialSuffix = ".partial";

    /// <summary>
    /// Opens a verified executable with the exact selected release and installation as review hints.
    /// </summary>
    /// <param name="target">The selected installation and signed release identity.</param>
    /// <param name="cancellationToken">The cancellable download and launch token.</param>
    /// <returns>The authenticated launch task.</returns>
    public Task LaunchAsync(StandaloneUpdateTarget target, CancellationToken cancellationToken)
        => LaunchAsync(target, StartProcess, cancellationToken);

    /// <summary>
    /// Resolves and authenticates the worker before giving control to the process launcher.
    /// </summary>
    /// <param name="target">The exact installation and signed release selection.</param>
    /// <param name="launch">The local process launcher.</param>
    /// <param name="cancellationToken">The prelaunch cancellation token.</param>
    /// <returns>The completed verified launch.</returns>
    internal async Task LaunchAsync(StandaloneUpdateTarget target, Action<ProcessStartInfo> launch, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(UpdaterText.RepositoryPolicyTheRepositoryConfigurationIsAmbiguousOrUnsupported);

        var cache = PlainPaths.Child(downloads.Root, CacheName);
        Directory.CreateDirectory(cache);
        var version = await FindVersionAsync(cancellationToken).ConfigureAwait(false);
        var manifest = await http.ReadAsync(new Uri(AssetUrl(version, ManifestName)), MetadataLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        var signature = await http.ReadAsync(new Uri(AssetUrl(version, SignatureName)), ReleaseJson.SignatureLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        var evidence = new ReleaseEvidence(manifest.Bytes, signature.Bytes, []);
        var worker = ReadVerified(verifier, evidence, version);
        var asset = AsAsset(worker);
        var archive = await downloads.GetAsync(asset, cancellationToken).ConfigureAwait(false);
        await using var lease = await AcquireAsync(PlainPaths.Child(cache, LockName), cancellationToken).ConfigureAwait(false);
        var watermarkPath = PlainPaths.Child(cache, Watermark);
        if (File.Exists(watermarkPath))
        {
            var previousEvidence = ReleaseJson.Parse<ReleaseEvidence>(File.ReadAllBytes(watermarkPath), 4 * MetadataLimit);
            var previous = ReadVerified(verifier, previousEvidence);
            var ordering = ReleaseProtocol.ParseVersion(worker.Version).CompareTo(ReleaseProtocol.ParseVersion(previous.Version));
            if (ordering < 0 || ordering == 0 && !evidence.Manifest.AsSpan().SequenceEqual(previousEvidence.Manifest))
                throw new InvalidDataException(UpdaterText.ReleaseDiscoveryTheReleaseFeedAttemptedToRollBackOrReplace);
        }

        var expected = ReleaseProtocol.Content(asset.Bytes, asset.Sha256);
        var path = await StageAsync(cache, worker.Version, archive, expected, cancellationToken).ConfigureAwait(false);

        using var ancestors = WindowsUpdateAccess.HoldAncestors(path);
        await using var executable = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (await TransactionStorage.ContentAsync(path, cancellationToken).ConfigureAwait(false) != expected)
            throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperChangedBeforeLaunch);

        TransactionStorage.WriteDurable(watermarkPath, ReleaseJson.Serialize(evidence));
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
        start.ArgumentList.Add(StandaloneUpdateTarget.Argument);
        start.ArgumentList.Add(target.InstallationRoot);
        start.ArgumentList.Add(target.Version);
        start.ArgumentList.Add(target.ManifestSha256);
        launch(start);
    }

    /// <summary>
    /// Opens the authenticated Windows executable while its verified bytes are held stable.
    /// </summary>
    /// <param name="start">The exact executable and review arguments.</param>
    private static void StartProcess(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new IOException(UpdaterText.UpdaterHandoffTheIndependentUpdaterCouldNotStart);
    }

    /// <summary>
    /// Installs one complete verified cached executable and repairs an interrupted or damaged local copy.
    /// </summary>
    /// <param name="cache">The private worker directory.</param>
    /// <param name="version">The authenticated independent worker version.</param>
    /// <param name="source">The checked content-addressed download.</param>
    /// <param name="expected">The exact signed length and SHA-256 digest.</param>
    /// <param name="cancellationToken">The staging cancellation token.</param>
    /// <returns>The verified executable path.</returns>
    private static async Task<string> StageAsync(string cache, string version, string source, GameFileContent expected, CancellationToken cancellationToken)
    {
        var directory = PlainPaths.Child(cache, version);
        var path = PlainPaths.Child(directory, ExecutableName);
        if (File.Exists(path) && await TransactionStorage.ContentAsync(path, cancellationToken).ConfigureAwait(false) == expected)
            return path;

        Directory.CreateDirectory(directory);
        var partial = PlainPaths.Child(directory, Guid.NewGuid().ToString(TransactionStorage.GuidFormat) + PartialSuffix);
        try
        {
            await TransactionStorage.CopyAsync(source, partial, expected, cancellationToken).ConfigureAwait(false);
            File.Move(partial, path, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }

        return path;
    }

    /// <summary>
    /// Binds the signed worker descriptor to its explicit version and fixed public artifact name.
    /// </summary>
    /// <param name="verifier">The independently configured release trust.</param>
    /// <param name="evidence">The exact worker descriptor and signatures.</param>
    /// <param name="expectedVersion">The public worker version, when known.</param>
    /// <returns>The authenticated version and binary fingerprint.</returns>
    internal static StandaloneUpdaterManifest ReadVerified(ReleaseVerifier verifier, ReleaseEvidence evidence, string? expectedVersion = null)
    {
        verifier.VerifySignature(evidence.Manifest, evidence.Signatures);
        var manifest = ReleaseJson.Parse<StandaloneUpdaterManifest>(evidence.Manifest, MetadataLimit);
        Validate(manifest, expectedVersion);
        return manifest;
    }

    /// <summary>
    /// Checks the fixed launch contract and the bounded binary fingerprint.
    /// </summary>
    /// <param name="manifest">The worker descriptor.</param>
    /// <param name="expectedVersion">The expected canonical worker version, if known.</param>
    internal static void Validate(StandaloneUpdaterManifest manifest, string? expectedVersion = null)
    {
        ReleaseProtocol.ParseVersion(manifest.Version);
        ReleaseProtocol.Content(manifest.Bytes, manifest.Sha256);
        if (manifest.DocumentType != DocumentType || manifest.SchemaVersion != 1 || manifest.Repository != ReleaseProtocol.Repository
            || manifest.Bytes is <= 0 or > 256L * 1024 * 1024 || expectedVersion is not null && expectedVersion != manifest.Version)
            throw new InvalidDataException(UpdaterText.SetupVersionsMissingMetadata);
    }

    /// <summary>
    /// Reads the newest public worker tag independently of the selected game release channel.
    /// </summary>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The exact version to authenticate.</returns>
    private async Task<string> FindVersionAsync(CancellationToken cancellationToken)
    {
        var response = await http.ReadAsync(new Uri(ReleaseProtocol.ReleaseListUrl), FeedLimit, cancellationToken: cancellationToken).ConfigureAwait(false);
        ReleaseJson.Validate(response.Bytes, FeedLimit);
        using var feed = JsonDocument.Parse(response.Bytes);
        Version? latest = null;
        string? version = null;
        foreach (var release in feed.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty(Draft, out var draft) || draft.ValueKind != JsonValueKind.False
                || !release.TryGetProperty(Prerelease, out var prerelease) || prerelease.ValueKind != JsonValueKind.True
                || !release.TryGetProperty(Tag, out var entry) || entry.ValueKind != JsonValueKind.String
                || entry.GetString() is not { } tag || !tag.StartsWith(TagPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var candidate = tag[TagPrefix.Length..];
            if (!Version.TryParse(candidate, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 || parsed.ToString(3) != candidate)
                continue;

            if (latest is null || parsed > latest)
            {
                latest = parsed;
                version = candidate;
            }
        }

        return version ?? throw new InvalidDataException(UpdaterText.SetupVersionsNewerSetupRequired);
    }

    /// <summary>
    /// Waits for concurrent tracker instances to finish selecting and staging their worker.
    /// </summary>
    /// <param name="path">The fixed private cache lock path.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The exclusive file lease.</returns>
    private static async Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            deadline.Token.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(100, deadline.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Maps a verified worker to the shared content-addressed download cache.
    /// </summary>
    /// <param name="manifest">The independently authenticated worker.</param>
    /// <returns>The bounded download descriptor.</returns>
    private static ReleaseAsset AsAsset(StandaloneUpdaterManifest manifest)
        => new(ExecutableName, WorkerRole, AssetUrl(manifest.Version, ExecutableName), manifest.Bytes, manifest.Sha256);

    /// <summary>
    /// Forms a version-bound download path in the existing credential-free release transport.
    /// </summary>
    /// <param name="version">The validated worker version.</param>
    /// <param name="name">The fixed asset basename.</param>
    /// <returns>The only accepted initial worker download URL.</returns>
    private static string AssetUrl(string version, string name)
        => ReleaseProtocol.ReleaseBaseUrl + "download/" + TagPrefix + version + '/' + name;
}

/// <summary>
/// Describes a standalone worker binary independently of any tracker or game payloads.
/// </summary>
/// <param name="DocumentType">The worker descriptor type.</param>
/// <param name="SchemaVersion">The stable bootstrap protocol version.</param>
/// <param name="Repository">The repository permitted to publish this worker.</param>
/// <param name="Version">The independent numeric worker version.</param>
/// <param name="Bytes">The exact executable length.</param>
/// <param name="Sha256">The executable SHA-256.</param>
internal sealed record StandaloneUpdaterManifest(string DocumentType, int SchemaVersion, string Repository, string Version, long Bytes, string Sha256);
