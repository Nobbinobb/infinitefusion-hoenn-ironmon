using System.IO.Compression;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.ReleaseTool;

/// <summary>
/// Produces and independently validates the updater's actual release contract from frozen package bytes.
/// </summary>
internal static class ReleaseBundle
{
    internal const string NotesName = "update-notes.md";
    internal const string TrustName = "update-trusted-keys.json";
    internal const string ZipExtension = ".zip";
    private const string InventoryPrefix = "ironmon-files-v";
    private const string GamePrefix = "game-files-";
    private const string JsonExtension = ".json";
    private const string RuntimeSuffix = "-runtime-required";
    private const string TrackerPrefix = "Ironmon-v";
    private const string WindowsSuffix = "-win-x64";
    private const string SetupPrefix = "Ironmon-Setup-v";
    private const string SetupSuffix = "-win-x64.exe";
    private const string HelperPrefix = "Ironmon-Updater-v";
    private const string CoreRuntime = "Ironmon Tracker/coreclr.dll";

    /// <summary>
    /// Returns the exact package name for a release and deployment flavor.
    /// </summary>
    /// <param name="version">The stable release version.</param>
    /// <param name="flavor">The explicit deployment flavor.</param>
    /// <returns>The immutable artifact basename.</returns>
    internal static string PackageName(string version, string flavor)
        => TrackerPrefix + version + WindowsSuffix + (flavor == ReleaseProtocol.RuntimeRequired ? RuntimeSuffix : string.Empty) + ZipExtension;

    /// <summary>
    /// Returns the exact disposable Setup artifact name.
    /// </summary>
    /// <param name="version">The stable release version.</param>
    /// <returns>The Setup basename.</returns>
    internal static string SetupName(string version)
        => SetupPrefix + version + SetupSuffix;

    /// <summary>
    /// Gets the versioned helper archive built from the same reviewed engine source.
    /// </summary>
    /// <param name="sequence">The monotonic release sequence.</param>
    /// <returns>The immutable helper archive name.</returns>
    internal static string HelperName(long sequence)
        => HelperPrefix + HelperVersion(sequence) + ZipExtension;

    /// <summary>
    /// Assigns immutable helper versions even when embedded game metadata changes between releases.
    /// </summary>
    /// <param name="sequence">The monotonic release sequence.</param>
    /// <returns>The helper build version at or above the minimum engine version.</returns>
    internal static string HelperVersion(long sequence)
    {
        var engine = ReleaseProtocol.ParseVersion(ReleaseProtocol.EngineVersion);
        return new Version(engine.Major, engine.Minor, checked(engine.Build + (int)sequence)).ToString(3);
    }

    /// <summary>
    /// Preserves the complete authenticated upstream tree while checking which entries can authorize game changes.
    /// </summary>
    /// <param name="game">The independently verified generated game baseline.</param>
    /// <param name="version">The selected game's display version.</param>
    /// <returns>The complete validated game inventory.</returns>
    internal static ReleaseFileInventory GameInventory(GameBaseline game, string version)
    {
        ReleaseProtocol.ParseVersion(version);
        var inventory = new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.GameScope, version, game.Commit, null, [.. game.Files.Select(file => new ReleaseFile(file.Path, file.Canonical.Sha256, file.Canonical.Length, ReleaseProtocol.GameScope, ReleaseProtocol.ReplacePolicy, file.WindowsText is null ? null : new ReleaseFileFingerprint(file.WindowsText.Length, file.WindowsText.Sha256)))]);
        ReleaseVerifier.ValidateFiles(inventory);
        return inventory;
    }

    /// <summary>
    /// Checks generated game evidence and restored release history before expensive tests or package builds begin.
    /// </summary>
    /// <param name="inventoryPath">The generated compressed game baseline.</param>
    /// <param name="manifestPath">Its independently checked companion manifest.</param>
    /// <param name="version">The selected game version.</param>
    /// <param name="history">Optional verified previous release data.</param>
    /// <param name="trust">The reviewed public trust file.</param>
    internal static void ValidateInputs(string inventoryPath, string manifestPath, string version, string? history, string trust)
    {
        using var inventory = File.OpenRead(inventoryPath);
        using var manifest = File.OpenRead(manifestPath);
        GameInventory(HoennGameBaseline.Load(inventory, manifest), version);
        if (history is null)
            return;

        if (File.Exists(PlainPaths.Child(history, ReleaseProtocol.ManifestName)))
        {
            Verify(history, trust, true, false);
        }
        else
        {
            LegacyHistory.Read(history);
        }
    }

    /// <summary>
    /// Creates unsigned immutable metadata; signing occurs only after candidate and upstream verification.
    /// </summary>
    /// <param name="request">The exact build inputs.</param>
    /// <returns>The complete unsigned manifest.</returns>
    internal static ReleaseManifest Create(BundleRequest request)
    {
        ReleaseProtocol.ParseVersion(request.Version);
        ReleaseProtocol.ParseVersion(request.GameVersion);
        ReleaseProtocol.ValidateCommit(request.GameCommit);
        var directory = PlainPaths.Full(request.Directory);
        var keys = ReadTrust(request.TrustFile);
        var verifier = new ReleaseVerifier(keys);
        using var gameBytes = File.OpenRead(request.GameInventory);
        using var gameManifest = File.OpenRead(request.GameManifest);
        var game = HoennGameBaseline.Load(gameBytes, gameManifest);
        if (game.Commit != request.GameCommit)
            throw new InvalidDataException("The generated game inventory differs from the workflow's selected commit.");

        if (File.Exists(PlainPaths.Child(directory, ReleaseProtocol.ManifestName)))
            throw new IOException("Update metadata already exists; release artifacts cannot be overwritten.");

        var assets = new List<ReleaseAsset>();
        var baselines = new Dictionary<string, AdoptionBaseline>(StringComparer.Ordinal);
        long sequence = 1;
        if (request.HistoryDirectory is { } legacy && !File.Exists(PlainPaths.Child(legacy, ReleaseProtocol.ManifestName)))
        {
            var imported = LegacyHistory.Read(legacy);
            if (ReleaseProtocol.ParseVersion(imported.Version) >= ReleaseProtocol.ParseVersion(request.Version))
                throw new InvalidDataException("Legacy history must precede the new release.");

            var gameAsset = GamePrefix + imported.Game.GameCommit + JsonExtension;
            WriteImmutable(directory, gameAsset, ReleaseJson.Serialize(imported.Game));
            assets.Add(Asset(directory, request.Version, gameAsset, ReleaseProtocol.GameInventoryRole));
            var packages = new List<LegacyPackage>();
            foreach (var inventory in imported.Packages)
            {
                var name = InventoryPrefix + imported.Version + '-' + inventory.Flavor + JsonExtension;
                WriteImmutable(directory, name, ReleaseJson.Serialize(inventory));
                assets.Add(Asset(directory, request.Version, name, ReleaseProtocol.LegacyRole));
                packages.Add(new LegacyPackage(imported.Version, inventory.Flavor!, name));
            }

            baselines.Add(imported.Game.GameCommit, new AdoptionBaseline(imported.Game.GameCommit, gameAsset, [.. packages]));
        }
        else if (request.HistoryDirectory is { } history)
        {
            var previous = Verify(history, request.TrustFile, true, false);
            if (ReleaseProtocol.ParseVersion(previous.IronmonVersion) >= ReleaseProtocol.ParseVersion(request.Version))
                throw new InvalidDataException("Release history must precede the new version.");

            sequence = checked(previous.ReleaseSequence + 1);
            foreach (var baseline in previous.AdoptionBaselines)
                baselines.Add(baseline.GameCommit, baseline);

            var historyMetadata = ReadMetadata(history, previous);
            foreach (var asset in previous.Assets.Where(asset => asset.Role is ReleaseProtocol.GameInventoryRole or ReleaseProtocol.LegacyRole or ReleaseProtocol.InventoryRole))
            {
                var bytes = ReadAsset(history, asset, historyMetadata);
                var inventory = verifier.VerifyInventory(previous, new InventoryEvidence(asset.Name, bytes));
                WriteImmutable(directory, asset.Name, bytes);
                assets.Add(Asset(directory, request.Version, asset.Name, asset.Role == ReleaseProtocol.InventoryRole ? ReleaseProtocol.LegacyRole : asset.Role));
                if (asset.Role == ReleaseProtocol.InventoryRole)
                {
                    var baseline = baselines[inventory.GameCommit];
                    baselines[inventory.GameCommit] = baseline with { LegacyPackages = [.. baseline.LegacyPackages, new LegacyPackage(inventory.Version, inventory.Flavor!, asset.Name)] };
                }
            }
        }

        var gameName = GamePrefix + game.Commit + JsonExtension;
        var gameInventory = GameInventory(game, request.GameVersion);
        if (!baselines.TryGetValue(game.Commit, out AdoptionBaseline? value))
        {
            WriteImmutable(directory, gameName, ReleaseJson.Serialize(gameInventory));
            assets.Add(Asset(directory, request.Version, gameName, ReleaseProtocol.GameInventoryRole));
            baselines.Add(game.Commit, new AdoptionBaseline(game.Commit, gameName, []));
        }
        else
        {
            var priorGame = ReleaseJson.Parse<ReleaseFileInventory>(File.ReadAllBytes(PlainPaths.Child(directory, value.GameFilesAsset)), ReleaseJson.InventoryLimit);
            if (!priorGame.Files.SequenceEqual(gameInventory.Files))
                throw new InvalidDataException("The same game commit cannot acquire different file fingerprints.");
        }

        foreach (var flavor in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var name = PackageName(request.Version, flavor);
            var files = BundleFiles.Archive(PlainPaths.Child(directory, name));
            var inventory = new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.IronmonScope, request.Version, game.Commit, flavor, files);
            var inventoryName = InventoryPrefix + request.Version + '-' + flavor + JsonExtension;
            WriteImmutable(directory, inventoryName, ReleaseJson.Serialize(inventory));
            assets.Add(Asset(directory, request.Version, name, ReleaseProtocol.TrackerRolePrefix + flavor));
            assets.Add(Asset(directory, request.Version, inventoryName, ReleaseProtocol.InventoryRole));
        }

        var helperEntry = HelperArchiveEntry(directory, HelperName(sequence));
        var helper = new ReleaseHelper(HelperVersion(sequence), helperEntry.Bytes, helperEntry.Sha256);
        assets.Add(Asset(directory, request.Version, SetupName(request.Version), ReleaseProtocol.SetupRole));
        assets.Add(Asset(directory, request.Version, NotesName, ReleaseProtocol.NotesRole));
        ReleaseMetadataFile[] metadataFiles = [.. assets.Where(asset => asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.LegacyRole or ReleaseProtocol.GameInventoryRole or ReleaseProtocol.NotesRole).OrderBy(asset => asset.Name, StringComparer.Ordinal).Select(asset => new ReleaseMetadataFile(asset.Name, File.ReadAllBytes(PlainPaths.Child(directory, asset.Name))))];
        if (request.GameDownloadBytes is { } gameDownloadBytes)
        {
            var sizes = new ReleaseDownloadSizeDocument(ReleaseDownloadSizes.DocumentType, 1, game.Commit, gameDownloadBytes);
            metadataFiles = [.. metadataFiles, new ReleaseMetadataFile(ReleaseDownloadSizes.FileName, ReleaseJson.Serialize(sizes))];
        }

        ReleaseDownloadSizes.Read(new ReleaseMetadataDocument(ReleaseMetadata.DocumentType, 1, metadataFiles), game.Commit, game.Commit);
        WriteImmutable(directory, ReleaseMetadata.Name, ReleaseJson.Serialize(new ReleaseMetadataDocument(ReleaseMetadata.DocumentType, 1, metadataFiles)));
        var metadataAsset = Asset(directory, request.Version, ReleaseMetadata.Name, ReleaseMetadata.Role);
        var container = new ReleaseContainer(metadataAsset.Name, metadataAsset.Bytes, metadataAsset.Sha256);
        assets = [.. assets.Select(asset => metadataFiles.Any(file => file.Name == asset.Name) ? asset with { Container = container, Url = metadataAsset.Url } : asset)];
        WriteImmutable(directory, TrustName, File.ReadAllBytes(request.TrustFile));
        var manifest = new ReleaseManifest(ReleaseProtocol.ReleaseDocument, 2, ReleaseProtocol.Repository, ReleaseProtocol.Stable, sequence, request.Version, request.Version, ReleaseProtocol.EngineVersion, NotesName, new ReleaseGame(ReleaseProtocol.GameRepository, ReleaseProtocol.GameBranch, request.GameVersion, game.Commit, [game.Commit], ReleaseProtocol.PreserveRuns), [.. assets.OrderBy(asset => asset.Name, StringComparer.Ordinal)], [.. baselines.Values.OrderBy(baseline => baseline.GameCommit, StringComparer.Ordinal)], helper);
        WriteImmutable(directory, ReleaseProtocol.ManifestName, ReleaseJson.Serialize(manifest));
        var verified = Verify(directory, request.TrustFile, false);
        File.Delete(PlainPaths.Child(directory, HelperName(sequence)));
        return verified;
    }

    /// <summary>
    /// Rechecks the signed wire contract, package ownership, every asset hash and exact helper parity.
    /// </summary>
    /// <param name="directory">The frozen release folder.</param>
    /// <param name="trustFile">Independent reviewed public trust.</param>
    /// <param name="requireSignature">Whether a detached release signature must already exist.</param>
    /// <param name="packages">Whether full program archives are present, instead of history metadata only.</param>
    /// <returns>The validated manifest.</returns>
    internal static ReleaseManifest Verify(string directory, string trustFile, bool requireSignature, bool packages = true)
    {
        var approvedTrust = ReadTrust(trustFile);
        var verifier = new ReleaseVerifier(approvedTrust);
        var bytes = File.ReadAllBytes(PlainPaths.Child(directory, ReleaseProtocol.ManifestName));
        var manifest = requireSignature ? verifier.Verify(bytes, File.ReadAllBytes(PlainPaths.Child(directory, ReleaseProtocol.SignatureName))) : ReleaseJson.Parse<ReleaseManifest>(bytes, ReleaseJson.ManifestLimit);
        ReleaseVerifier.ValidateManifest(manifest, null);
        if (manifest.Assets.Where(asset => asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.LegacyRole or ReleaseProtocol.GameInventoryRole).Sum(asset => asset.Bytes) > ReleaseJson.InventoryLimit)
            throw new InvalidDataException("The release's historical inventories exceed the installed clients' metadata budget.");

        var metadata = ReadMetadata(directory, manifest);
        ReleaseDownloadSizes.Read(metadata, manifest.Game.PreferredCommit, manifest.Game.PreferredCommit);
        var inventories = new List<ReleaseFileInventory>();
        foreach (var asset in manifest.Assets)
        {
            var inventory = asset.Role is ReleaseProtocol.InventoryRole or ReleaseProtocol.LegacyRole or ReleaseProtocol.GameInventoryRole;
            if (!packages && !inventory)
                continue;

            if (asset.Container is null && Asset(directory, manifest.IronmonVersion, asset.Name, asset.Role) != asset)
                throw new InvalidDataException("A release asset differs from its immutable manifest.");

            if (inventory)
            {
                inventories.Add(verifier.VerifyInventory(manifest, new InventoryEvidence(asset.Name, ReadAsset(directory, asset, metadata))));
            }
            else if (asset.Container is not null)
            {
                _ = ReadAsset(directory, asset, metadata);
            }
        }

        if (!packages)
            return manifest;

        var helper = HelperEntry(directory, manifest);
        var trustPath = PlainPaths.Child(directory, TrustName);
        var trust = File.Exists(trustPath) ? File.ReadAllBytes(trustPath) : BundleFiles.ReadDocument(PlainPaths.Child(directory, manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + ReleaseProtocol.RuntimeRequired).Name), BundleFiles.TrustPath);
        var candidateTrust = UpdaterTrust.ReadKeys(trust);
        if (candidateTrust.Count != approvedTrust.Count || candidateTrust.Any(key => !approvedTrust.TryGetValue(key.Key, out var approved) || !key.Value.AsSpan().SequenceEqual(approved)))
            throw new InvalidDataException("Candidate public trust differs from approved source.");

        foreach (var flavor in new[] { ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired })
        {
            var archive = PlainPaths.Child(directory, manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.TrackerRolePrefix + flavor).Name);
            var inventory = inventories.Single(item => item.Scope == ReleaseProtocol.IronmonScope && item.Version == manifest.IronmonVersion && item.Flavor == flavor);
            var actual = BundleFiles.Archive(archive);
            if (!actual.SequenceEqual(inventory.Files))
                throw new InvalidDataException("A player archive differs from its ownership inventory.");

            var inner = ReleaseJson.Parse<ReleaseFileInventory>(BundleFiles.ReadDocument(archive, BundleFiles.OwnershipPath), ReleaseJson.InventoryLimit);
            if (inner.DocumentType != inventory.DocumentType || inner.SchemaVersion != inventory.SchemaVersion || inner.Scope != inventory.Scope || inner.Version != inventory.Version || inner.GameCommit != inventory.GameCommit || inner.Flavor != flavor || !inner.Files.SequenceEqual(actual.Where(file => file.Path != BundleFiles.OwnershipPath)))
                throw new InvalidDataException("The packaged ownership document identifies different payload bytes.");

            var bundledHelper = actual.SingleOrDefault(file => file.Path == BundleFiles.HelperPath);
            if (bundledHelper is null || bundledHelper.Bytes != helper.Bytes || bundledHelper.Sha256 != helper.Sha256 || !actual.Any(file => file.Path == BundleFiles.NoticesPath) || !trust.AsSpan().SequenceEqual(BundleFiles.ReadDocument(archive, BundleFiles.TrustPath)))
                throw new InvalidDataException("A package has missing or inconsistent updater, trust or third-party notices.");

            if ((flavor == ReleaseProtocol.SelfContained) != actual.Any(file => file.Path == CoreRuntime) || !actual.Any(file => file.Path == FileManagementPolicy.BootstrapPath))
                throw new InvalidDataException("A release has an inconsistent runtime flavor or lacks the early game guard.");
        }

        return manifest;
    }

    /// <summary>
    /// Reads the exact single-file recovery archive contract.
    /// </summary>
    /// <param name="directory">The release directory.</param>
    /// <param name="manifest">The validated release manifest.</param>
    /// <returns>The independently hashed helper executable.</returns>
    private static ReleaseFile HelperEntry(string directory, ReleaseManifest manifest)
    {
        if (manifest.Helper is { } helper)
            return new ReleaseFile(UpdaterHandoff.HelperFileName, helper.Sha256, helper.Bytes, ReleaseProtocol.IronmonScope, ReleaseProtocol.ReplacePolicy);

        return HelperArchiveEntry(directory, manifest.Assets.Single(asset => asset.Role == ReleaseProtocol.UpdaterRole).Name);
    }

    /// <summary>
    /// Reads the temporary producer archive or an older release's separately published helper.
    /// </summary>
    /// <param name="directory">The release directory.</param>
    /// <param name="name">The exact helper archive basename.</param>
    /// <returns>The helper executable fingerprint.</returns>
    private static ReleaseFile HelperArchiveEntry(string directory, string name)
    {
        using var zip = ZipFile.OpenRead(PlainPaths.Child(directory, name));
        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != UpdaterHandoff.HelperFileName || zip.Entries[0].Length is <= 0 or > 256L * 1024 * 1024)
            throw new InvalidDataException("A helper release must contain exactly one bounded executable.");

        var type = (zip.Entries[0].ExternalAttributes >> 16) & 0xF000;
        if (type is not (0 or 0x8000) || (zip.Entries[0].ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A helper executable cannot be an archive link.");

        using var stream = zip.Entries[0].Open();
        return BundleFiles.Entry(UpdaterHandoff.HelperFileName, zip.Entries[0].Length, stream);
    }

    /// <summary>
    /// Reads a logical metadata document from its authenticated container or legacy standalone file.
    /// </summary>
    /// <param name="directory">The candidate or restored history directory.</param>
    /// <param name="asset">The signed document identity.</param>
    /// <param name="metadata">The shared container already authenticated for this manifest, if available.</param>
    /// <returns>The original document bytes.</returns>
    internal static byte[] ReadAsset(string directory, ReleaseAsset asset, ReleaseMetadataDocument? metadata = null)
    {
        if (asset.Container is not null)
        {
            if (metadata is not null)
                return ReleaseMetadata.Read(metadata, asset);

            var containerPath = PlainPaths.Child(directory, asset.Container.Name);
            if (asset.Container.Bytes is <= 0 or > ReleaseMetadata.Limit || new FileInfo(containerPath).Length != asset.Container.Bytes)
                throw new InvalidDataException("The metadata container has an invalid length.");

            return ReleaseMetadata.Read(File.ReadAllBytes(containerPath), asset);
        }

        return File.ReadAllBytes(PlainPaths.Child(directory, asset.Name));
    }

    /// <summary>
    /// Authenticates the shared metadata once before reading a release's inventories.
    /// </summary>
    /// <param name="directory">The candidate, publication, or history directory.</param>
    /// <param name="manifest">The validated release with one unambiguous container.</param>
    /// <returns>The authenticated metadata, or null for an older standalone layout.</returns>
    private static ReleaseMetadataDocument? ReadMetadata(string directory, ReleaseManifest manifest)
    {
        var container = manifest.Assets.FirstOrDefault(asset => asset.Container is not null)?.Container;
        if (container is null)
            return null;

        var path = PlainPaths.Child(directory, container.Name);
        if (container.Bytes is <= 0 or > ReleaseMetadata.Limit || new FileInfo(path).Length != container.Bytes)
            throw new InvalidDataException("The metadata container has an invalid length.");

        return ReleaseMetadata.Parse(File.ReadAllBytes(path), container);
    }

    /// <summary>
    /// Loads validated public trust and refuses to produce unusable release packages with empty trust.
    /// </summary>
    /// <param name="path">The independently configured public trust file.</param>
    /// <returns>The nonempty key set.</returns>
    internal static IReadOnlyDictionary<string, byte[]> ReadTrust(string path)
    {
        var keys = UpdaterTrust.ReadKeys(File.ReadAllBytes(path));
        if (keys.Count == 0)
            throw new InvalidDataException("Provision dedicated updater public keys before producing a release candidate.");

        return keys;
    }

    /// <summary>
    /// Hashes one flat immutable artifact under the fixed release URL.
    /// </summary>
    /// <param name="directory">The owned output directory.</param>
    /// <param name="version">The release version.</param>
    /// <param name="name">The validated basename.</param>
    /// <param name="role">The explicit protocol role.</param>
    /// <returns>The exact artifact identity.</returns>
    internal static ReleaseAsset Asset(string directory, string version, string name, string role)
    {
        ReleaseProtocol.ValidateAssetName(name);
        using var file = File.OpenRead(PlainPaths.Child(directory, name));
        return new ReleaseAsset(name, role, ReleaseProtocol.AssetUrl(version, name), file.Length, BundleFiles.Hash(file));
    }

    /// <summary>
    /// Writes new metadata or verifies identical historical bytes without permitting replacement.
    /// </summary>
    /// <param name="directory">The release output.</param>
    /// <param name="name">The safe immutable basename.</param>
    /// <param name="bytes">The exact document bytes.</param>
    internal static void WriteImmutable(string directory, string name, byte[] bytes)
    {
        var path = PlainPaths.Child(directory, name);
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                throw new IOException("An immutable metadata asset already has different bytes.");
        }
        else
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(bytes);
            output.Flush(true);
        }
    }
}
