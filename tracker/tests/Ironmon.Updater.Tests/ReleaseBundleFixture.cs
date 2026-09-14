using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ironmon.ReleaseTool;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Builds real release documents around isolated synthetic A, B and C payloads without production credentials.
/// </summary>
internal sealed class ReleaseBundleFixture : IDisposable, IArtifactSource
{
    internal const string VersionA = "99.0.1";
    internal const string VersionB = "99.0.2";
    internal const string VersionC = "99.0.3";
    internal const string CommitA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string CommitB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    internal const string KeyId = "fixture-only";
    internal const string GamePath = "Game.ini";
    internal const string ProtectedGamePath = "Graphics/CustomBattlers/spritesheets/.DS_Store";
    internal const string ScriptPath = "Data/Scripts/997_Ironmon/fixture.rb";
    internal const string DataPath = "Data/Ironmon/fixture.dat";
    internal const string RuntimePath = "Ironmon Tracker/coreclr.dll";
    internal const string OldProfile = "Data/Ironmon/generation_profiles/fixture-a.json";
    internal const string NewProfile = "Data/Ironmon/generation_profiles/fixture-b.json";
    private const string TrustName = "fixture-public-keys.json";
    private const string InstallationName = "installation";
    private const string CacheName = "cache";
    private const string PayloadPrefix = "payload-";
    private const string GameVersion = "6.8.2";
    private const string BaselineName = "fixture-game.json.gz";
    private const string CompanionName = "fixture-game.manifest.json";
    private const string Mode = "100644";
    private const string RuntimeJson = "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}";
    private const string SelfContainedJson = "{\"runtimeOptions\":{\"includedFrameworks\":[]}}";
    private readonly TestWorkspace _workspace = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, string> _assets = new(StringComparer.Ordinal);
    private readonly List<string> _downloads = [];

    /// <summary>
    /// Writes only the ephemeral public key into the fixture workspace.
    /// </summary>
    internal ReleaseBundleFixture()
    {
        File.WriteAllBytes(Trust, ReleaseJson.Serialize(new Dictionary<string, string> { [KeyId] = Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo()) }));
    }

    /// <summary>
    /// Gets the independently provisioned fixture public trust.
    /// </summary>
    internal string Trust => _workspace.PathFor(TrustName);

    /// <summary>
    /// Gets the actual transport requests made while installing and updating the fixture.
    /// </summary>
    internal IReadOnlyList<string> Downloads => _downloads;

    /// <summary>
    /// Gets the isolated installation, never the user's game directory.
    /// </summary>
    internal string Root => _workspace.PathFor(InstallationName);

    /// <summary>
    /// Gets the client verifier using the same fixture key as the producer.
    /// </summary>
    internal ReleaseVerifier Verifier => new(ReleaseBundle.ReadTrust(Trust));

    /// <summary>
    /// Resolves one synthetic release directory.
    /// </summary>
    /// <param name="version">The synthetic version.</param>
    /// <returns>The isolated bundle path.</returns>
    internal string DirectoryFor(string version) => _workspace.PathFor(version);

    /// <summary>
    /// Packages both deployment flavors and generates metadata using the actual release producer.
    /// </summary>
    /// <param name="version">The synthetic release version.</param>
    /// <param name="history">The preceding signed bundle, when present.</param>
    /// <param name="commit">The selected synthetic game commit.</param>
    /// <param name="customize">An optional deliberate package defect introduced before ownership is hashed.</param>
    /// <param name="gameDownloadBytes">The synthetic measured game estimate, or null for older releases.</param>
    /// <returns>The exact producer inputs, allowing deliberate corruption tests before signing.</returns>
    internal BundleRequest Build(string version, string? history = null, string commit = CommitA, Action<string, string>? customize = null, long? gameDownloadBytes = 4096)
    {
        var directory = DirectoryFor(version);
        Directory.CreateDirectory(directory);
        var sequence = history is null || !File.Exists(Path.Combine(history, ReleaseProtocol.ManifestName)) ? 1 : ReleaseBundle.Verify(history, Trust, true).ReleaseSequence + 1;
        var helper = Encoding.UTF8.GetBytes(ReleaseBundle.HelperVersion(sequence));
        using (var zip = ZipFile.Open(Path.Combine(directory, ReleaseBundle.HelperName(sequence)), ZipArchiveMode.Create))
        using (var entry = zip.CreateEntry(UpdaterHandoff.HelperFileName).Open())
            entry.Write(helper);

        File.WriteAllBytes(Path.Combine(directory, ReleaseBundle.SetupName(version)), Encoding.UTF8.GetBytes(version));
        File.WriteAllText(Path.Combine(directory, ReleaseBundle.NotesName), "Synthetic release notes.");
        foreach (var flavor in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var payload = _workspace.PathFor(PayloadPrefix + version + '-' + flavor);
            foreach (var path in new[] { ScriptPath, DataPath, UpdaterHandoff.TrackerRelativePath, FileManagementPolicy.BootstrapPath })
                Write(payload, path, Encoding.UTF8.GetBytes(version));

            Write(payload, version == VersionA ? OldProfile : NewProfile, [1]);
            Write(payload, BundleFiles.HelperPath, helper);
            Write(payload, BundleFiles.TrustPath, File.ReadAllBytes(Trust));
            Write(payload, BundleFiles.NoticesPath, [1]);
            Write(payload, TrackerRuntimeCompatibility.ConfigurationPath, Encoding.UTF8.GetBytes(flavor == ReleaseProtocol.SelfContained ? SelfContainedJson : RuntimeJson));
            if (flavor == ReleaseProtocol.SelfContained)
                Write(payload, RuntimePath, [1]);

            customize?.Invoke(payload, flavor);
            BundleFiles.WriteOwnership(payload, version, flavor, commit);
            ZipFile.CreateFromDirectory(payload, Path.Combine(directory, ReleaseBundle.PackageName(version, flavor)));
        }

        var baseline = _workspace.PathFor(version + '-' + BaselineName);
        var companion = _workspace.PathFor(version + '-' + CompanionName);
        var fingerprint = new GameFileContent(1, Convert.ToHexString(SHA256.HashData(new byte[] { 1 })));
        var game = new GameBaseline(commit, [new GameBaselineFile(GamePath, Mode, commit, fingerprint, null), new GameBaselineFile(ProtectedGamePath, Mode, commit, fingerprint, null)]);
        using (var output = File.Create(baseline))
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize))
            JsonSerializer.Serialize(gzip, game);

        File.WriteAllBytes(companion, JsonSerializer.SerializeToUtf8Bytes(new { Commit = commit, FileCount = game.Files.Count, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline))) }));
        var request = new BundleRequest(directory, version, GameVersion, commit, baseline, companion, Trust, history, gameDownloadBytes);
        ReleaseBundle.Create(request);
        return request;
    }

    /// <summary>
    /// Signs exact generated bytes with a disposable key that never enters a file or environment variable.
    /// </summary>
    /// <param name="version">The already built synthetic release.</param>
    /// <returns>The signed evidence consumed by installed clients.</returns>
    internal ReleaseEvidence Sign(string version)
    {
        var directory = DirectoryFor(version);
        var privateBytes = _key.ExportPkcs8PrivateKey();
        try
        {
            ReleaseSigning.Sign(directory, Trust, KeyId, privateBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privateBytes);
        }

        var manifest = ReleaseBundle.Verify(directory, Trust, true);
        foreach (var asset in manifest.Assets)
            _assets[asset.Url] = Path.Combine(directory, asset.Container?.Name ?? asset.Name);

        return new ReleaseEvidence(File.ReadAllBytes(Path.Combine(directory, ReleaseProtocol.ManifestName)), File.ReadAllBytes(Path.Combine(directory, ReleaseProtocol.SignatureName)), []);
    }

    /// <summary>
    /// Supplies the actual preparation engine with offline assets and explicit fixture runtime availability.
    /// </summary>
    /// <returns>The shared installer and updater workflow.</returns>
    internal IronmonOnlyUpdate Coordinator()
        => new(Verifier, new ReleaseDownloadStore(_workspace.PathFor(CacheName), this), SignedReleaseFixture.Runtime());

    /// <summary>
    /// Writes bounded fixture content under a caller-owned directory.
    /// </summary>
    /// <param name="root">The fixture directory.</param>
    /// <param name="relative">The trusted relative file path.</param>
    /// <param name="bytes">The synthetic content.</param>
    internal static void Write(string root, string relative, byte[] bytes)
    {
        var path = PlainPaths.Child(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Serves only locally generated artifacts through their exact fixed release URLs.
    /// </summary>
    /// <param name="source">The manifest-bound address.</param>
    /// <param name="destination">The caller-owned destination.</param>
    /// <param name="maximumBytes">The signed size limit.</param>
    /// <param name="cancellationToken">The copy token.</param>
    /// <returns>The completed bounded copy.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        _downloads.Add(source.AbsoluteUri);
        await using var input = File.OpenRead(_assets[source.AbsoluteUri]);
        Assert.True(input.Length <= maximumBytes);
        await input.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>
    /// Destroys the ephemeral key and the complete owned fixture workspace.
    /// </summary>
    public void Dispose()
    {
        _key.Dispose();
        _workspace.Dispose();
    }
}
