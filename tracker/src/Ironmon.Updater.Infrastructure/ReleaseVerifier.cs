using System.Security.Cryptography;
using Ironmon.Updater.Core;
using static Ironmon.Updater.Infrastructure.ReleaseProtocol;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Verifies exact manifest signatures against embedded trust, then validates cross-document release identities.
/// </summary>
public sealed class ReleaseVerifier
{
    private const string CurveOid = "1.2.840.10045.3.1.7";
    private const string TagPrefix = "v";
    private const string LegacyDocuments = "README.md|LICENSE|INSTALLATION.md|RELEASE_NOTES.md|THIRD_PARTY_NOTICES.md|OPEN-SANS-LICENSE.txt";
    private static readonly HashSet<string> _legacyDocuments = new(LegacyDocuments.Split('|'), StringComparer.OrdinalIgnoreCase);
    private static readonly FileManagementPolicy _filePolicy = new();
    private readonly IReadOnlyDictionary<string, byte[]> _keys;

    /// <summary>
    /// Copies dedicated updater public keys from trusted application configuration, never from a release or journal.
    /// </summary>
    /// <param name="publicKeys">Key identifiers mapped to P-256 SubjectPublicKeyInfo bytes.</param>
    public ReleaseVerifier(IReadOnlyDictionary<string, byte[]> publicKeys)
    {
        _keys = publicKeys.ToDictionary(pair => pair.Key, pair => (byte[])pair.Value.Clone(), StringComparer.Ordinal);
        foreach (var key in _keys.Values)
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out var consumed);
            if (consumed != key.Length || ecdsa.KeySize != 256 || ecdsa.ExportParameters(false).Curve.Oid.Value != CurveOid)
                throw new InvalidDataException(UpdaterText.ReleaseVerifierUpdaterTrustRequiresDedicatedP256PublicKeys);
        }
    }

    /// <summary>
    /// Authenticates exact manifest bytes before parsing their fields or following their asset references.
    /// </summary>
    /// <param name="manifestBytes">The exact UTF-8 manifest.</param>
    /// <param name="signatureBytes">The bounded detached signature document.</param>
    /// <param name="expectedTag">The exact stable tag observed at the fixed GitHub endpoint, if discovering a release.</param>
    /// <returns>A detached authenticated manifest with validated identities and asset roles.</returns>
    public ReleaseManifest Verify(byte[] manifestBytes, byte[] signatureBytes, string? expectedTag = null)
    {
        if (manifestBytes.Length == 0 || manifestBytes.Length > ReleaseJson.ManifestLimit || _keys.Count == 0)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierNoTrustedUpdaterSignatureIsAvailableForThisRelease);

        var signatures = ReleaseJson.Parse<ReleaseSignatures>(signatureBytes, ReleaseJson.SignatureLimit);
        if (signatures.DocumentType != SignatureDocument || signatures.SchemaVersion != 1 || signatures.Signatures.Length is < 1 or > 4 || signatures.Signatures.Select(item => item.KeyId).Distinct(StringComparer.Ordinal).Count() != signatures.Signatures.Length)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheDetachedSignatureDocumentIsUnsupportedOrAmbiguous);

        var verified = false;
        foreach (var signature in signatures.Signatures)
        {
            if (signature.Algorithm != Algorithm || signature.KeyId.Length is < 1 or > 64 || signature.KeyId.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseSignatureAlgorithmOrKeyIdentifierIsInvalid);

            var bytes = Convert.FromBase64String(signature.Signature);
            if (bytes.Length != 64 || Convert.ToBase64String(bytes) != signature.Signature)
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseSignatureIsNotCanonicalIEEEP1363Data);

            if (!_keys.TryGetValue(signature.KeyId, out var key))
                continue;

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(key, out _);
            verified |= ecdsa.VerifyData(manifestBytes, bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        if (!verified)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheUpdateManifestHasNoValidSignatureFromAn);

        var manifest = ReleaseJson.Parse<ReleaseManifest>(manifestBytes, ReleaseJson.ManifestLimit);
        ValidateManifest(manifest, expectedTag);
        return manifest;
    }

    /// <summary>
    /// Rechecks a persisted inventory against the authenticated manifest before exposing managed files.
    /// </summary>
    /// <param name="manifest">The authenticated release.</param>
    /// <param name="evidence">The exact referenced inventory bytes.</param>
    /// <returns>The strictly validated inventory.</returns>
    public ReleaseFileInventory VerifyInventory(ReleaseManifest manifest, InventoryEvidence evidence)
    {
        var asset = manifest.Assets.SingleOrDefault(item => item.Name == evidence.AssetName) ?? throw new InvalidDataException(UpdaterText.ReleaseVerifierTheInventoryIsNotReferencedByThisSignedRelease);
        if (asset.Role is not (InventoryRole or LegacyRole or GameInventoryRole) || evidence.Bytes.LongLength != asset.Bytes || TransactionStorage.Hash(evidence.Bytes) != Content(asset.Bytes, asset.Sha256).Sha256)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheInventoryBytesDoNotMatchTheSignedRelease);

        var inventory = ReleaseJson.Parse<ReleaseFileInventory>(evidence.Bytes, ReleaseJson.InventoryLimit);
        if (inventory.DocumentType != InventoryDocument || inventory.SchemaVersion != 1 || inventory.Files.Length is < 1 or > 200000)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheFileInventorySchemaOrEntryCountIsUnsupported);

        ValidateCommit(inventory.GameCommit);
        ParseVersion(inventory.Version);
        if (!manifest.AdoptionBaselines.Any(baseline => baseline.GameCommit == inventory.GameCommit))
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheFileInventoryRefersToAnUnsupportedGameCommit);

        if (asset.Role == GameInventoryRole)
        {
            if (inventory.Scope != GameScope || inventory.Flavor is not null || !manifest.AdoptionBaselines.Any(item => item.GameCommit == inventory.GameCommit && item.GameFilesAsset == asset.Name))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheGameInventoryDoesNotMatchItsSignedBaseline);
        }
        else
        {
            ParseVersion(inventory.Version);
            if (inventory.Scope != IronmonScope || inventory.Flavor is not (SelfContained or RuntimeRequired))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheIronmonInventoryHasAnUnsupportedScopeOrFlavor);

            if (asset.Role == InventoryRole && (inventory.Version != manifest.IronmonVersion || !manifest.Game.SupportedCommits.Contains(inventory.GameCommit, StringComparer.Ordinal)))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierThePackageInventoryVersionDoesNotMatchTheSigned);

            if (asset.Role == LegacyRole && !manifest.AdoptionBaselines.Any(item => item.GameCommit == inventory.GameCommit && item.LegacyPackages.Any(package => package.Version == inventory.Version && package.Flavor == inventory.Flavor && package.FilesAsset == asset.Name)))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheLegacyInventoryIsNotExplicitlyAuthorizedForThis);
        }

        ValidateFiles(inventory);
        return inventory;
    }

    /// <summary>
    /// Validates release-wide roles, references, repository and version agreement after signature verification.
    /// </summary>
    /// <param name="manifest">The signed document.</param>
    /// <param name="expectedTag">The stable discovery tag, if present.</param>
    internal static void ValidateManifest(ReleaseManifest manifest, string? expectedTag)
    {
        ParseVersion(manifest.IronmonVersion);
        ParseVersion(manifest.MinimumEngineVersion);
        ParseVersion(manifest.Game.VersionLabel);
        if (manifest.DocumentType != ReleaseDocument || manifest.SchemaVersion is not (1 or 2) || manifest.Repository != Repository || manifest.Channel != Stable || manifest.ReleaseSequence <= 0 || manifest.TrackerVersion != manifest.IronmonVersion || (expectedTag is not null && expectedTag != TagPrefix + manifest.IronmonVersion))
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseIdentityVersionChannelOrSchemaIsUnsupported);

        if (manifest.Game.Repository != GameRepository || manifest.Game.Branch != GameBranch || manifest.Game.ActiveRunPolicy is not (PreserveRuns or FinishRun) || manifest.Game.SupportedCommits.Length is < 1 or > 32 || manifest.Game.SupportedCommits.Distinct(StringComparer.Ordinal).Count() != manifest.Game.SupportedCommits.Length || !manifest.Game.SupportedCommits.Contains(manifest.Game.PreferredCommit, StringComparer.Ordinal))
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheSignedGameCompatibilityPolicyIsInvalid);

        foreach (var commit in manifest.Game.SupportedCommits)
            ValidateCommit(commit);

        if (manifest.Assets.Length is < 6 or > 256 || manifest.Assets.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Assets.Length)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseAssetListIsIncompleteOrAmbiguous);

        foreach (var asset in manifest.Assets)
        {
            ValidateAssetName(asset.Name);
            Content(asset.Bytes, asset.Sha256);
            if (asset.Bytes == 0 || asset.Url != AssetUrl(manifest.IronmonVersion, asset.Container?.Name ?? asset.Name) || asset.Role is not (TrackerRolePrefix + SelfContained or TrackerRolePrefix + RuntimeRequired or InventoryRole or LegacyRole or GameInventoryRole or SetupRole or UpdaterRole or NotesRole))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheAssetURLSizeOrRoleIsOutsideThe);

            var embedded = asset.Role is InventoryRole or LegacyRole or GameInventoryRole or NotesRole;
            if (manifest.SchemaVersion == 1 && asset.Container is not null || manifest.SchemaVersion == 2 && embedded != (asset.Container is not null))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheMetadataStorageDiffersFromTheSignedReleaseSchema);

            if (asset.Container is { } container)
            {
                Content(container.Bytes, container.Sha256);
                if (container.Name != ReleaseMetadata.Name || container.Bytes is <= 0 or > ReleaseMetadata.Limit)
                    throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseMetadataContainerIsUnsupportedOrOversized);
            }
        }

        foreach (var role in new[] { TrackerRolePrefix + SelfContained, TrackerRolePrefix + RuntimeRequired, SetupRole, NotesRole })
        {
            if (manifest.Assets.Count(asset => asset.Role == role) != 1)
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseContainsDuplicateOrMissingRequiredAssetRoles);
        }

        if (manifest.Assets.Count(asset => asset.Role == InventoryRole) != 2 || manifest.Assets.Single(asset => asset.Role == NotesRole).Name != manifest.ReleaseNotesAsset)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseMustBindBothPackageInventoriesAndIts);

        string helperVersion;
        if (manifest.SchemaVersion == 1)
        {
            if (manifest.Helper is not null || manifest.Assets.Count(asset => asset.Role == UpdaterRole) != 1)
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheLegacyReleaseRequiresOneSeparateHelperArchive);

            helperVersion = HelperVersion(manifest.Assets.Single(asset => asset.Role == UpdaterRole).Name);
        }
        else
        {
            var helper = manifest.Helper ?? throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseLacksAnAuthenticatedEmbeddedHelper);
            Content(helper.Bytes, helper.Sha256);
            if (helper.Bytes is <= 0 or > 256L * 1024 * 1024 || manifest.Assets.Any(asset => asset.Role == UpdaterRole) || manifest.Assets.Where(asset => asset.Container is not null).Select(asset => asset.Container).Distinct().Count() != 1)
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheCompactReleaseRequiresOneMetadataContainerAndAn);

            helperVersion = helper.Version;
        }

        if (ParseVersion(helperVersion) < ParseVersion(manifest.MinimumEngineVersion))
            throw new InvalidDataException(UpdaterText.ReleaseVerifierTheBundledUpdaterIsOlderThanTheReleaseS);

        if (manifest.AdoptionBaselines.Length > 100 || manifest.Game.SupportedCommits.Any(commit => !manifest.AdoptionBaselines.Any(baseline => baseline.GameCommit == commit)) || manifest.AdoptionBaselines.Select(item => item.GameCommit).Distinct(StringComparer.Ordinal).Count() != manifest.AdoptionBaselines.Length)
            throw new InvalidDataException(UpdaterText.ReleaseVerifierEachSupportedCommitRequiresOneUnambiguousGameInventory);

        foreach (var baseline in manifest.AdoptionBaselines)
        {
            ValidateCommit(baseline.GameCommit);
            if (!manifest.Assets.Any(asset => asset.Name == baseline.GameFilesAsset && asset.Role == GameInventoryRole) || baseline.LegacyPackages.Length > 64 || baseline.LegacyPackages.Select(package => (package.Version, package.Flavor)).Distinct().Count() != baseline.LegacyPackages.Length)
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseAdoptionReferencesAreInvalidOrAmbiguous);

            foreach (var package in baseline.LegacyPackages)
            {
                if (ParseVersion(package.Version) >= ParseVersion(manifest.IronmonVersion) || package.Flavor is not (SelfContained or RuntimeRequired) || !manifest.Assets.Any(asset => asset.Name == package.FilesAsset && asset.Role == LegacyRole))
                    throw new InvalidDataException(UpdaterText.ReleaseVerifierTheReleaseReferencesAnInvalidLegacyPackageInventory);
            }
        }
    }

    /// <summary>
    /// Converts authenticated inventory entries into planner ownership, preserving legacy root documents.
    /// </summary>
    /// <param name="inventory">The validated inventory.</param>
    /// <returns>The exact managed program files.</returns>
    internal static ManagedFile[] ManagedFiles(ReleaseFileInventory inventory)
        => [.. inventory.Files.Where(file => inventory.Scope == GameScope ? IsManagedGamePath(file.Path) : !IsLegacyDocument(file.Path)).Select(file => new ManagedFile(file.Path, Content(file.Bytes, file.Sha256), inventory.Scope == IronmonScope ? ManagedFileOwner.Ironmon : ManagedFileOwner.Game, file.Policy == RetainPolicy ? ManagedFilePolicy.Retain : ManagedFilePolicy.Replace))];

    /// <summary>
    /// Excludes protected player state from game ownership without removing it from complete upstream inventory evidence.
    /// </summary>
    /// <param name="path">The validated game inventory path.</param>
    /// <returns>Whether the updater may manage this game file.</returns>
    internal static bool IsManagedGamePath(string path)
        => !IsLegacyDocument(path) && !_filePolicy.IsProtected(path);

    /// <summary>
    /// Identifies signed distribution documents whose existing root copies remain user-owned during updates.
    /// </summary>
    /// <param name="path">The inventory path.</param>
    /// <returns>Whether the file is an explicit legacy root document.</returns>
    internal static bool IsLegacyDocument(string path)
        => _legacyDocuments.Contains(path);

    /// <summary>
    /// Validates every path and rejects duplicate, prefix-colliding or improperly owned program files.
    /// </summary>
    /// <param name="inventory">The authenticated inventory.</param>
    internal static void ValidateFiles(ReleaseFileInventory inventory)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in inventory.Files)
        {
            ReleaseArchive.ValidateRelativePath(file.Path);
            Content(file.Bytes, file.Sha256);
            if (file.WindowsText is { } windowsText)
            {
                Content(windowsText.Bytes, windowsText.Sha256);
                if (inventory.Scope != GameScope || file.Policy != ReplacePolicy)
                    throw new InvalidDataException(UpdaterText.ReleaseVerifierOnlyReplaceableGameTextFilesMayDeclareASigned);
            }

            total = checked(total + file.Bytes);
            if (!paths.Add(file.Path) || total > 8L * 1024 * 1024 * 1024 || file.Owner != inventory.Scope || file.Policy is not (ReplacePolicy or RetainPolicy) || (inventory.Scope == GameScope && file.Policy != ReplacePolicy))
                throw new InvalidDataException(UpdaterText.ReleaseVerifierTheInventoryHasDuplicatePathsExcessiveSizeOrInconsistent);
        }

        foreach (var path in paths)
        {
            for (var slash = path.LastIndexOf('/'); slash >= 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (paths.Contains(path[..slash]))
                    throw new InvalidDataException(UpdaterText.ReleaseVerifierTheInventoryContainsAFileDirectoryPrefixCollision);
            }
        }

        new FileUpdatePlanner(new FileManagementPolicy()).Create([], [], ManagedFiles(inventory));
        if (inventory.Scope == IronmonScope && (!paths.Contains(UpdaterHandoff.TrackerRelativePath) || !paths.Any(path => path.StartsWith(IronmonOnlyUpdate.ScriptRoot, StringComparison.Ordinal)) || !paths.Any(path => path.StartsWith(IronmonOnlyUpdate.DataRoot, StringComparison.Ordinal)) || paths.Contains(IronmonOnlyUpdate.ReceiptPath)))
            throw new InvalidDataException(UpdaterText.ReleaseVerifierThePackageMustContainMatchingTrackerScriptsAndData);
    }
}
