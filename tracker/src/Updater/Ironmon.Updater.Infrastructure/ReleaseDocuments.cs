namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Describes a signed stable release and its hash-bound artifacts.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="DocumentType">The document discriminator.</param>
/// <param name="SchemaVersion">The manifest schema.</param>
/// <param name="Repository">The fixed public repository.</param>
/// <param name="Channel">The stable release channel.</param>
/// <param name="ReleaseSequence">The monotonic signed publication sequence.</param>
/// <param name="IronmonVersion">The script and data version.</param>
/// <param name="TrackerVersion">The matching tracker version.</param>
/// <param name="MinimumEngineVersion">The oldest supported updater engine.</param>
/// <param name="ReleaseNotesAsset">The bound release notes asset name.</param>
/// <param name="Game">The approved game compatibility policy.</param>
/// <param name="Assets">The unique hash-bound release assets.</param>
/// <param name="AdoptionBaselines">The signed legacy adoption references.</param>
/// <param name="Helper">The authenticated helper embedded in both player packages for compact releases.</param>
public sealed record ReleaseManifest(string DocumentType, int SchemaVersion, string Repository, string Channel, long ReleaseSequence, string IronmonVersion, string TrackerVersion, string MinimumEngineVersion, string ReleaseNotesAsset, ReleaseGame Game, ReleaseAsset[] Assets, AdoptionBaseline[] AdoptionBaselines, ReleaseHelper? Helper = null);

/// <summary>
/// Binds compatibility to approved game commits.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="Repository">The fixed upstream Git URL.</param>
/// <param name="Branch">The expected upstream branch.</param>
/// <param name="VersionLabel">The display-only game version.</param>
/// <param name="PreferredCommit">The preferred approved commit.</param>
/// <param name="SupportedCommits">The exact supported commit identities.</param>
/// <param name="ActiveRunPolicy">The explicit run preservation policy.</param>
public sealed record ReleaseGame(string Repository, string Branch, string VersionLabel, string PreferredCommit, string[] SupportedCommits, string ActiveRunPolicy);

/// <summary>
/// Binds one release artifact to its role, address and bytes.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="Name">The unique asset basename.</param>
/// <param name="Role">The protocol asset role.</param>
/// <param name="Url">The exact release download URL.</param>
/// <param name="Bytes">The signed byte length.</param>
/// <param name="Sha256">The signed SHA-256 digest.</param>
/// <param name="Container">The single hash-bound metadata download containing this logical asset, when bundled.</param>
public sealed record ReleaseAsset(string Name, string Role, string Url, long Bytes, string Sha256, ReleaseContainer? Container = null);

/// <summary>
/// Authenticates the shared metadata file before any embedded document is used.
/// </summary>
/// <remarks>
/// Constructs an immutable container identity from the signed release manifest.
/// </remarks>
/// <param name="Name">The fixed metadata basename.</param>
/// <param name="Bytes">The exact downloaded length.</param>
/// <param name="Sha256">The exact downloaded digest.</param>
public sealed record ReleaseContainer(string Name, long Bytes, string Sha256);

/// <summary>
/// Identifies the recovery executable already included in each player package.
/// </summary>
/// <remarks>
/// Constructs the signed executable identity without requiring a separate helper download.
/// </remarks>
/// <param name="Version">The immutable helper version.</param>
/// <param name="Bytes">The exact executable length.</param>
/// <param name="Sha256">The exact executable digest.</param>
public sealed record ReleaseHelper(string Version, long Bytes, string Sha256);

/// <summary>
/// References inventories that authenticate adoption without trusting local ownership records.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="GameCommit">The approved game baseline.</param>
/// <param name="GameFilesAsset">The signed game inventory reference.</param>
/// <param name="LegacyPackages">The explicitly supported older Ironmon packages.</param>
public sealed record AdoptionBaseline(string GameCommit, string GameFilesAsset, LegacyPackage[] LegacyPackages);

/// <summary>
/// References a signed inventory for an older package flavor.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="Version">The prior stable version.</param>
/// <param name="Flavor">The prior deployment flavor.</param>
/// <param name="FilesAsset">The hash-bound prior inventory asset.</param>
public sealed record LegacyPackage(string Version, string Flavor, string FilesAsset);

/// <summary>
/// Describes exact package files under signed release ownership.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="DocumentType">The inventory discriminator.</param>
/// <param name="SchemaVersion">The inventory schema.</param>
/// <param name="Scope">The game or Ironmon ownership scope.</param>
/// <param name="Version">The component version.</param>
/// <param name="GameCommit">The associated game commit.</param>
/// <param name="Flavor">The deployment flavor, or null for a game inventory.</param>
/// <param name="Files">The exact ordinary file entries.</param>
public sealed record ReleaseFileInventory(string DocumentType, int SchemaVersion, string Scope, string Version, string GameCommit, string? Flavor, ReleaseFile[] Files);

/// <summary>
/// Binds one installed file to exact bytes and ownership policy.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="Path">The canonical installation-relative path.</param>
/// <param name="Sha256">The exact content digest.</param>
/// <param name="Bytes">The file length.</param>
/// <param name="Owner">The game or Ironmon owner.</param>
/// <param name="Policy">The replace or immutable-retain policy.</param>
/// <param name="WindowsText">An optional signed CRLF representation for a game text file.</param>
public sealed record ReleaseFile(string Path, string Sha256, long Bytes, string Owner, string Policy, ReleaseFileFingerprint? WindowsText = null);

/// <summary>
/// Describes an explicitly authenticated alternate representation without normalizing arbitrary local edits.
/// </summary>
/// <remarks>
/// Constructs a wire fingerprint for a game file's known Windows checkout representation.
/// </remarks>
/// <param name="Bytes">The exact byte length.</param>
/// <param name="Sha256">The exact SHA-256 digest.</param>
public sealed record ReleaseFileFingerprint(long Bytes, string Sha256);

/// <summary>
/// Carries detached signatures over the exact raw manifest bytes.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="DocumentType">The signature discriminator.</param>
/// <param name="SchemaVersion">The signature schema.</param>
/// <param name="Signatures">At most four independent signatures.</param>
public sealed record ReleaseSignatures(string DocumentType, int SchemaVersion, ReleaseSignature[] Signatures);

/// <summary>
/// Identifies one embedded trust key and detached signature.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="KeyId">The embedded key lookup identifier.</param>
/// <param name="Algorithm">The ECDSA protocol algorithm.</param>
/// <param name="Signature">The base64 IEEE P1363 signature.</param>
public sealed record ReleaseSignature(string KeyId, string Algorithm, string Signature);

/// <summary>
/// Persists authenticated documents for independent helper revalidation.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="Manifest">The exact signed UTF-8 manifest.</param>
/// <param name="Signatures">The detached signature document.</param>
/// <param name="Inventories">The hash-bound selected inventories.</param>
public sealed record ReleaseEvidence(byte[] Manifest, byte[] Signatures, InventoryEvidence[] Inventories);

/// <summary>
/// Stores an inventory without turning its cache location into authority.
/// </summary>
/// <remarks>
/// Constructs the wire document; consumers must authenticate and validate it before use.
/// </remarks>
/// <param name="AssetName">The signed manifest asset name.</param>
/// <param name="Bytes">The exact inventory bytes.</param>
public sealed record InventoryEvidence(string AssetName, byte[] Bytes);
