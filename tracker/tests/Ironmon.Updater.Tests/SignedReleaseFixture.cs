using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Ironmon.Updater.Infrastructure;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Builds disposable signed releases whose private key exists only inside the fixture.
/// </summary>
internal sealed class SignedReleaseFixture : IArtifactSource, IDisposable
{
    internal const string VersionA = "0.8.0";
    internal const string VersionB = "0.9.0";
    internal const string VersionC = "0.10.0";
    internal const string Commit = "1111111111111111111111111111111111111111";
    internal const string KeyId = "fixture-key";
    internal const string Script = "Data/Scripts/997_Ironmon/main.rb";
    internal const string Data = "Data/Ironmon/catalog.json";
    internal const string Obsolete = "Data/Scripts/997_Ironmon/obsolete.rb";
    internal const string Profile = "Data/Ironmon/generation_profiles/history.json";
    internal const string Game = "Game.exe";
    internal const string Save = "File A.rxdata";
    internal const string Settings = "Ironmon Tracker/settings.json";
    internal const string ReadmeDocument = "README.md";
    internal const string InstallationDocument = "INSTALLATION.md";
    internal const string LicenseDocument = "LICENSE";
    internal const string FontLicenseDocument = "OPEN-SANS-LICENSE.txt";
    internal const string ReleaseNotesDocument = "RELEASE_NOTES.md";
    internal const string ThirdPartyDocument = "THIRD_PARTY_NOTICES.md";
    internal const string RuntimeJson = "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}}}";
    private const string SelfContainedJson = "{\"runtimeOptions\":{\"includedFrameworks\":[{\"name\":\"Microsoft.NETCore.App\",\"version\":\"10.0.0\"}]}}";
    private const string CoreRuntime = "Ironmon Tracker/coreclr.dll";
    private const string ArchivePrefix = "tracker-";
    private const string InventoryPrefix = "files-";
    private const string LegacyPrefix = "legacy-";
    private const string ZipExtension = ".zip";
    private const string JsonExtension = ".json";
    private const string HelperName = "Ironmon-Updater-v1.0.1.zip";
    private const string SetupName = "setup.exe";
    private const string NotesName = "notes.md";
    private const string GameInventoryName = "game.json";
    private const string PreviousGameInventoryName = "game-before.json";
    private const string RootName = "installation";
    private const string CacheName = "downloads";
    internal const string HostRelativePath = "../../../../Ironmon.Updater.CrashHost/bin/";
#if DEBUG
    internal const string BuildConfiguration = "Debug";
#else
    internal const string BuildConfiguration = "Release";
#endif
    internal const string HostSuffix = "/net10.0/Ironmon.Updater.CrashHost.exe";
    private const string TrackerDirectory = "Ironmon Tracker/";
    private const string EmbeddedHelper = TrackerDirectory + "Updater/Ironmon.Updater.exe";
    private readonly TestWorkspace _workspace = new();
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Dictionary<string, byte[]> _assets = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the disposable installation root.
    /// </summary>
    internal string Root => _workspace.PathFor(RootName);

    /// <summary>
    /// Gets the separate download directory.
    /// </summary>
    internal string Cache => _workspace.PathFor(CacheName);

    /// <summary>
    /// Gets the fixture verifier whose key is independent of transaction files.
    /// </summary>
    internal ReleaseVerifier Verifier => new(new Dictionary<string, byte[]> { [KeyId] = _key.ExportSubjectPublicKeyInfo() });

    /// <summary>
    /// Gets the test-only public key passed independently to the disposable signed helper process.
    /// </summary>
    internal string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

    /// <summary>
    /// Gets the selected release evidence.
    /// </summary>
    internal ReleaseEvidence Evidence { get; private set; } = null!;

    /// <summary>
    /// Gets the manifest used to generate signatures.
    /// </summary>
    internal ReleaseManifest Manifest { get; private set; } = null!;

    /// <summary>
    /// Gets or sets download corruption injection.
    /// </summary>
    internal bool CorruptArchive { get; set; }

    /// <summary>
    /// Gets or sets network failure injection.
    /// </summary>
    internal bool Offline { get; set; }

    /// <summary>
    /// Gets all requested assets to prove no game archive or Git acquisition occurs.
    /// </summary>
    internal List<Uri> Requests { get; } = [];

    /// <summary>
    /// Gets the signed distribution documents excluded from installation ownership.
    /// </summary>
    internal static IReadOnlyList<string> DistributionDocuments { get; } = [ReadmeDocument, InstallationDocument, LicenseDocument, FontLicenseDocument, ReleaseNotesDocument, ThirdPartyDocument];

    /// <summary>
    /// Creates a real planner-compatible installation and complete synthetic signed release.
    /// </summary>
    /// <param name="flavor">The initial package flavor.</param>
    /// <param name="runnableTracker">Whether to include the actual fixture host as the installed tracker.</param>
    /// <param name="distributionDocuments">Whether packages include the root documents shipped in real releases.</param>
    internal SignedReleaseFixture(string flavor = ReleaseProtocol.RuntimeRequired, bool runnableTracker = false, bool distributionDocuments = false)
    {
        var assets = new List<ReleaseAsset>();
        var inventories = new List<InventoryEvidence>();
        var legacy = new List<LegacyPackage>();
        foreach (var item in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var before = Package(VersionA, item, runnableTracker);
            var after = Package(VersionB, item, runnableTracker);
            if (distributionDocuments)
            {
                foreach (var document in DistributionDocuments)
                {
                    before.Add(document, Encoding.UTF8.GetBytes(VersionA));
                    after.Add(document, Encoding.UTF8.GetBytes(VersionB));
                }
            }

            if (item == flavor)
            {
                foreach (var file in before)
                    Write(file.Key, file.Value);
            }

            assets.Add(Asset(ArchivePrefix + item + ZipExtension, ReleaseProtocol.TrackerRolePrefix + item, Archive(after)));
            var currentName = InventoryPrefix + item + JsonExtension;
            var current = Inventory(after, VersionB, item);
            AddInventory(currentName, ReleaseProtocol.InventoryRole, current, assets, inventories);
            var priorName = LegacyPrefix + item + JsonExtension;
            AddInventory(priorName, ReleaseProtocol.LegacyRole, Inventory(before, VersionA, item), assets, inventories);
            legacy.Add(new LegacyPackage(VersionA, item, priorName));
        }

        byte[] game = [7];
        Write(Game, game);
        Write(Save, [42]);
        Write(Settings, [43]);
        var gameInventory = new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.GameScope, VersionA, Commit, null, [Entry(Game, game, ReleaseProtocol.GameScope)]);
        AddInventory(GameInventoryName, ReleaseProtocol.GameInventoryRole, gameInventory, assets, inventories);
        assets.Add(Asset(HelperName, ReleaseProtocol.UpdaterRole, Archive(new Dictionary<string, byte[]> { [UpdaterHandoff.HelperFileName] = [5] })));
        assets.Add(Asset(SetupName, ReleaseProtocol.SetupRole, [6]));
        assets.Add(Asset(NotesName, ReleaseProtocol.NotesRole, [8]));
        var compatible = new ReleaseGame(ReleaseProtocol.GameRepository, ReleaseProtocol.GameBranch, VersionA, Commit, [Commit], ReleaseProtocol.PreserveRuns);
        Manifest = new ReleaseManifest(ReleaseProtocol.ReleaseDocument, 1, ReleaseProtocol.Repository, ReleaseProtocol.Stable, 2, VersionB, VersionB, ReleaseProtocol.EngineVersion, NotesName, compatible, [.. assets], [new AdoptionBaseline(Commit, GameInventoryName, [.. legacy])]);
        Evidence = Sign(Manifest, [.. inventories]);
    }

    /// <summary>
    /// Signs exact caller-provided bytes for parser boundary fixtures.
    /// </summary>
    /// <param name="bytes">The exact document bytes.</param>
    /// <returns>The detached fixture signature.</returns>
    internal byte[] Signature(byte[] bytes)
    {
        var signature = Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return ReleaseJson.Serialize(new ReleaseSignatures(ReleaseProtocol.SignatureDocument, 1, [new ReleaseSignature(KeyId, ReleaseProtocol.Algorithm, signature)]));
    }

    /// <summary>
    /// Publishes a signed fixture policy requiring run completion before installing the release.
    /// </summary>
    internal void RequireCompletedRun()
    {
        Manifest = Manifest with { Game = Manifest.Game with { ActiveRunPolicy = ReleaseProtocol.FinishRun } };
        Evidence = Sign(Manifest, Evidence.Inventories);
    }

    /// <summary>
    /// Signs a modified fixture manifest while retaining its explicit inventories.
    /// </summary>
    /// <param name="manifest">The manifest to authenticate.</param>
    /// <param name="inventories">The associated exact documents.</param>
    /// <returns>The independently signed evidence.</returns>
    internal ReleaseEvidence Sign(ReleaseManifest manifest, InventoryEvidence[] inventories)
    {
        var bytes = ReleaseJson.Serialize(manifest);
        return new ReleaseEvidence(bytes, Signature(bytes), inventories);
    }

    /// <summary>
    /// Authenticates selected executable bytes as the helper for isolated administrator-worker tests.
    /// </summary>
    /// <param name="bytes">The actual fixture host bytes, never a production signing key.</param>
    internal void UseHelper(byte[] bytes)
    {
        var archive = Archive(new Dictionary<string, byte[]> { [UpdaterHandoff.HelperFileName] = bytes });
        _assets[HelperName] = archive;
        var inventories = Evidence.Inventories.ToDictionary(item => item.AssetName, StringComparer.Ordinal);
        foreach (var flavor in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var package = Package(VersionB, flavor, false);
            package[EmbeddedHelper] = bytes;
            var archiveName = ArchivePrefix + flavor + ZipExtension;
            var inventoryName = InventoryPrefix + flavor + JsonExtension;
            _assets[archiveName] = Archive(package);
            _assets[inventoryName] = ReleaseJson.Serialize(Inventory(package, VersionB, flavor));
            inventories[inventoryName] = new InventoryEvidence(inventoryName, _assets[inventoryName]);
        }

        Manifest = Manifest with { Assets = [.. Manifest.Assets.Select(asset => asset with { Bytes = _assets[asset.Name].LongLength, Sha256 = TransactionStorage.Hash(_assets[asset.Name]) })] };
        Evidence = Sign(Manifest, [.. inventories.Values]);
    }

    /// <summary>
    /// Re-signs display-only release notes for production renderer and localization fixtures.
    /// </summary>
    /// <param name="notes">The exact plain-text notes to authenticate.</param>
    internal void UseNotes(string notes)
    {
        var bytes = Encoding.UTF8.GetBytes(notes);
        _assets[NotesName] = bytes;
        Manifest = Manifest with { Assets = [.. Manifest.Assets.Select(asset => asset.Name == NotesName ? asset with { Bytes = bytes.LongLength, Sha256 = TransactionStorage.Hash(bytes) } : asset)] };
        Evidence = Sign(Manifest, Evidence.Inventories);
    }

    /// <summary>
    /// Rebinds the synthetic release to a real disposable Git history with an explicitly historical adoption baseline.
    /// </summary>
    /// <param name="before">The complete signed historical game inventory.</param>
    /// <param name="target">The complete signed approved target inventory.</param>
    internal void UseGameHistory(ReleaseFileInventory before, ReleaseFileInventory target)
    {
        var inventories = new List<InventoryEvidence>();
        var assets = Manifest.Assets.ToDictionary(asset => asset.Name, StringComparer.Ordinal);
        foreach (var item in Evidence.Inventories)
        {
            var inventory = ReleaseJson.Parse<ReleaseFileInventory>(item.Bytes, ReleaseJson.InventoryLimit);
            inventory = inventory.Scope == ReleaseProtocol.GameScope ? target : inventory with { GameCommit = inventory.Version == VersionA ? before.GameCommit : target.GameCommit };
            var bytes = ReleaseJson.Serialize(inventory);
            _assets[item.AssetName] = bytes;
            assets[item.AssetName] = assets[item.AssetName] with { Bytes = bytes.LongLength, Sha256 = TransactionStorage.Hash(bytes) };
            inventories.Add(new InventoryEvidence(item.AssetName, bytes));
        }

        var previousBytes = ReleaseJson.Serialize(before);
        assets.Add(PreviousGameInventoryName, Asset(PreviousGameInventoryName, ReleaseProtocol.GameInventoryRole, previousBytes));
        inventories.Add(new InventoryEvidence(PreviousGameInventoryName, previousBytes));
        var legacy = Manifest.AdoptionBaselines[0].LegacyPackages;
        Manifest = Manifest with { Game = Manifest.Game with { PreferredCommit = target.GameCommit, SupportedCommits = [target.GameCommit] }, Assets = [.. assets.Values], AdoptionBaselines = [new AdoptionBaseline(before.GameCommit, PreviousGameInventoryName, legacy), new AdoptionBaseline(target.GameCommit, GameInventoryName, [])] };
        Evidence = Sign(Manifest, [.. inventories]);
    }

    /// <summary>
    /// Publishes a third synthetic mod version on the same game to verify maintenance after disposable Setup removal.
    /// </summary>
    internal void AdvanceVersion()
    {
        var assets = Manifest.Assets.ToDictionary(asset => asset.Name, StringComparer.Ordinal);
        var inventories = Evidence.Inventories.ToDictionary(item => item.AssetName, StringComparer.Ordinal);
        foreach (var flavor in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var package = Package(VersionC, flavor, false);
            var archiveName = ArchivePrefix + flavor + ZipExtension;
            var inventoryName = InventoryPrefix + flavor + JsonExtension;
            _assets[archiveName] = Archive(package);
            _assets[inventoryName] = ReleaseJson.Serialize(Inventory(package, VersionC, flavor) with { GameCommit = Manifest.Game.PreferredCommit });
            foreach (var name in new[] { archiveName, inventoryName })
                assets[name] = assets[name] with { Bytes = _assets[name].LongLength, Sha256 = TransactionStorage.Hash(_assets[name]) };

            inventories[inventoryName] = new InventoryEvidence(inventoryName, _assets[inventoryName]);
        }

        Manifest = Manifest with { IronmonVersion = VersionC, TrackerVersion = VersionC, ReleaseSequence = Manifest.ReleaseSequence + 1, Assets = [.. assets.Values.Select(asset => asset with { Url = ReleaseProtocol.AssetUrl(VersionC, asset.Name) })] };
        Evidence = Sign(Manifest, [.. inventories.Values]);
    }

    /// <summary>
    /// Builds the selected forward-update request.
    /// </summary>
    /// <param name="flavor">The package flavor.</param>
    /// <returns>The selected disposable installation.</returns>
    internal IronmonUpdateRequest Request(string flavor = ReleaseProtocol.RuntimeRequired)
        => new(Root, VersionA, flavor, flavor, Commit, false, []);

    /// <summary>
    /// Creates the real preparation workflow with fixture transport and runtime availability.
    /// </summary>
    /// <param name="missingRuntime">Whether shared frameworks are absent.</param>
    /// <returns>The production coordinator configured only with isolated test inputs.</returns>
    internal IronmonOnlyUpdate Coordinator(bool missingRuntime = false)
        => new(Verifier, new ReleaseDownloadStore(Cache, this), Runtime(missingRuntime));

    /// <summary>
    /// Creates a real configuration checker with controlled installed framework versions.
    /// </summary>
    /// <param name="missing">Whether to report no installed framework.</param>
    /// <returns>The runtime prerequisite checker.</returns>
    internal static TrackerRuntimeCompatibility Runtime(bool missing = false)
        => new(_ => missing ? [] : [new Version(10, 0, 1)]);

    /// <summary>
    /// Writes bytes only under this disposable installation.
    /// </summary>
    /// <param name="relative">The fixture path.</param>
    /// <param name="bytes">The desired content.</param>
    internal void Write(string relative, byte[] bytes)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>
    /// Supplies only explicitly named release assets and records requests.
    /// </summary>
    /// <param name="source">The repository-bound asset.</param>
    /// <param name="destination">The caller-owned output.</param>
    /// <param name="maximumBytes">The signed limit.</param>
    /// <param name="cancellationToken">The download token.</param>
    /// <returns>A completed copy or an injected transport failure.</returns>
    public async Task CopyToAsync(Uri source, Stream destination, long maximumBytes, CancellationToken cancellationToken)
    {
        Requests.Add(source);
        if (Offline)
            throw new HttpRequestException("The fixture network is unavailable.");

        var name = Uri.UnescapeDataString(source.Segments[^1]);
        var bytes = CorruptArchive && name.StartsWith(ArchivePrefix, StringComparison.Ordinal) ? new byte[] { 255 } : _assets[name];
        await destination.WriteAsync(bytes, cancellationToken);
    }

    /// <summary>
    /// Creates matching tracker, script and data bytes with obsolete-file and retained-profile examples.
    /// </summary>
    /// <param name="version">The component version.</param>
    /// <param name="flavor">The runtime flavor.</param>
    /// <param name="runnableTracker">Whether to include an executable fixture tracker.</param>
    /// <returns>The complete synthetic package.</returns>
    private static Dictionary<string, byte[]> Package(string version, string flavor, bool runnableTracker)
    {
        var bytes = Encoding.UTF8.GetBytes(version);
        var files = new Dictionary<string, byte[]> { [UpdaterHandoff.TrackerRelativePath] = bytes, [Script] = bytes, [Data] = bytes, [Profile] = [9], [TrackerRuntimeCompatibility.ConfigurationPath] = Encoding.UTF8.GetBytes(flavor == ReleaseProtocol.SelfContained ? SelfContainedJson : RuntimeJson) };
        if (version != VersionA)
            files.Add(FileManagementPolicy.BootstrapPath, bytes);

        if (flavor == ReleaseProtocol.SelfContained)
            files.Add(CoreRuntime, [10]);

        if (version == VersionA)
            files.Add(Obsolete, bytes);

        if (runnableTracker)
        {
            var host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, HostRelativePath + BuildConfiguration + HostSuffix));
            foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(host)!))
                files[TrackerDirectory + Path.GetFileName(path)] = File.ReadAllBytes(path);

            files[UpdaterHandoff.TrackerRelativePath] = File.ReadAllBytes(host);
        }

        return files;
    }

    /// <summary>
    /// Creates an exact archive, including unsafe paths when explicitly supplied by a negative fixture.
    /// </summary>
    /// <param name="files">The archive entries.</param>
    /// <returns>The ZIP bytes.</returns>
    internal static byte[] Archive(Dictionary<string, byte[]> files)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                using var stream = archive.CreateEntry(file.Key).Open();
                stream.Write(file.Value);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Creates an exact package ownership inventory.
    /// </summary>
    /// <param name="files">The complete package.</param>
    /// <param name="version">The component version.</param>
    /// <param name="flavor">The package flavor.</param>
    /// <returns>The signed inventory input.</returns>
    private static ReleaseFileInventory Inventory(Dictionary<string, byte[]> files, string version, string flavor)
        => new(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.IronmonScope, version, Commit, flavor, [.. files.Select(file => Entry(file.Key, file.Value))]);

    /// <summary>
    /// Creates a byte-exact file entry with immutable profile retention.
    /// </summary>
    /// <param name="path">The relative file path.</param>
    /// <param name="bytes">The expected bytes.</param>
    /// <param name="owner">The authenticated owner.</param>
    /// <returns>The exact inventory entry.</returns>
    internal static ReleaseFile Entry(string path, byte[] bytes, string owner = ReleaseProtocol.IronmonScope)
        => new(path, TransactionStorage.Hash(bytes), bytes.LongLength, owner, path == Profile ? ReleaseProtocol.RetainPolicy : ReleaseProtocol.ReplacePolicy);

    /// <summary>
    /// Binds an inventory document into the release assets and portable evidence.
    /// </summary>
    /// <param name="name">The flat asset name.</param>
    /// <param name="role">The authorized role.</param>
    /// <param name="inventory">The exact ownership document.</param>
    /// <param name="assets">The release asset accumulator.</param>
    /// <param name="evidence">The evidence accumulator.</param>
    private void AddInventory(string name, string role, ReleaseFileInventory inventory, List<ReleaseAsset> assets, List<InventoryEvidence> evidence)
    {
        var bytes = ReleaseJson.Serialize(inventory);
        assets.Add(Asset(name, role, bytes));
        evidence.Add(new InventoryEvidence(name, bytes));
    }

    /// <summary>
    /// Registers a hash-bound fixture asset at the fixed repository release URL.
    /// </summary>
    /// <param name="name">The flat asset name.</param>
    /// <param name="role">The authorized role.</param>
    /// <param name="bytes">The exact asset bytes.</param>
    /// <returns>The signed asset reference.</returns>
    private ReleaseAsset Asset(string name, string role, byte[] bytes)
    {
        _assets.Add(name, bytes);
        return new ReleaseAsset(name, role, ReleaseProtocol.AssetUrl(VersionB, name), bytes.LongLength, TransactionStorage.Hash(bytes));
    }

    /// <summary>
    /// Disposes the private fixture key and only the owned disposable filesystem.
    /// </summary>
    public void Dispose()
    {
        _key.Dispose();
        _workspace.Dispose();
    }
}
