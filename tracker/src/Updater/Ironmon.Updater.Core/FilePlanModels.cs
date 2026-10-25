namespace Ironmon.Updater.Core;

/// <summary>
/// Identifies the publisher responsible for an individually inventoried file.
/// </summary>
public enum ManagedFileOwner
{
    /// <summary>
    /// The official game owns the file.
    /// </summary>
    Game = 0,
    /// <summary>
    /// The Ironmon package owns the file.
    /// </summary>
    Ironmon = 1
}

/// <summary>
/// Defines whether a known file may be replaced or must remain immutable.
/// </summary>
public enum ManagedFilePolicy
{
    /// <summary>
    /// Compare the file using ordinary three-way update rules.
    /// </summary>
    Replace = 0,
    /// <summary>
    /// Add missing content, retain existing content and reject differing bytes.
    /// </summary>
    Retain = 1
}

/// <summary>
/// Describes one file in an authenticated, complete component inventory.
/// </summary>
/// <remarks>
/// Initializes immutable ownership and content expectations; construction does not authenticate a manifest.
/// </remarks>
/// <param name="Path">The installation-relative path using forward slashes.</param>
/// <param name="Content">The canonical bytes to stage when a write is necessary.</param>
/// <param name="Owner">The trusted component owner.</param>
/// <param name="Policy">The replacement or retention policy.</param>
/// <param name="AlternateContent">An explicitly authenticated alternative byte representation, such as CRLF text.</param>
public sealed record ManagedFile(string Path, GameFileContent Content, ManagedFileOwner Owner, ManagedFilePolicy Policy = ManagedFilePolicy.Replace, GameFileContent? AlternateContent = null);

/// <summary>
/// Captures one real installation entry, including empty directories.
/// </summary>
/// <remarks>
/// Initializes a snapshot entry whose absence from a complete snapshot means the path is missing.
/// </remarks>
/// <param name="Path">The installation-relative path using forward slashes.</param>
/// <param name="Content">The exact file fingerprint, or null for a directory.</param>
public sealed record LocalFileEntry(string Path, GameFileContent? Content);

/// <summary>
/// Describes the proposed treatment of one inventoried or unrelated file path.
/// </summary>
public enum FilePlanAction
{
    /// <summary>
    /// Preserve the observed state, including an intentionally missing file.
    /// </summary>
    Keep = 0,
    /// <summary>
    /// Install the exact target bytes.
    /// </summary>
    Write = 1,
    /// <summary>
    /// Remove an obsolete, previously managed file after backup.
    /// </summary>
    Delete = 2,
    /// <summary>
    /// Block the complete update until the conflict is resolved.
    /// </summary>
    Conflict = 3
}

/// <summary>
/// Explains why a file cannot be updated automatically.
/// </summary>
public enum FileConflictKind
{
    /// <summary>
    /// No conflict exists at this path.
    /// </summary>
    None = 0,
    /// <summary>
    /// Both the local bytes and the target changed from the baseline.
    /// </summary>
    LocalModification = 1,
    /// <summary>
    /// The player removed a file that upstream changed.
    /// </summary>
    LocalDeletion = 2,
    /// <summary>
    /// Upstream removed a file whose local bytes were modified.
    /// </summary>
    ModifiedObsoleteFile = 3,
    /// <summary>
    /// A new managed file would overwrite an unrelated existing file.
    /// </summary>
    NewFileCollision = 4,
    /// <summary>
    /// An immutable profile or retained file has differing content.
    /// </summary>
    RetainedContent = 5,
    /// <summary>
    /// A file/directory transition would remove content that this plan preserves.
    /// </summary>
    PathCollision = 6
}

/// <summary>
/// Records a deterministic three-way decision with the exact inspected state.
/// </summary>
/// <remarks>
/// Initializes a review entry; replacing a conflict requires explicit consent for that entry and snapshot.
/// </remarks>
/// <param name="Path">The reviewed relative file path.</param>
/// <param name="Baseline">The prior managed file, or null for a new path.</param>
/// <param name="Local">The inspected file or directory, or null when absent.</param>
/// <param name="Target">The new managed file, or null when obsolete or unrelated.</param>
/// <param name="Action">The proposed action.</param>
/// <param name="Conflict">The reason for a blocked action.</param>
/// <param name="CanReplaceAfterBackup">Whether explicit consent can replace this file conflict after verified backup.</param>
public sealed record FilePlanEntry(string Path, ManagedFile? Baseline, LocalFileEntry? Local, ManagedFile? Target, FilePlanAction Action, FileConflictKind Conflict = FileConflictKind.None, bool CanReplaceAfterBackup = false);

/// <summary>
/// Defines ordered filesystem operations for the later transaction engine.
/// </summary>
public enum FileOperationKind
{
    /// <summary>
    /// Back up and delete one exact file.
    /// </summary>
    DeleteFile = 0,
    /// <summary>
    /// Remove one empty directory without recursive deletion.
    /// </summary>
    RemoveDirectory = 1,
    /// <summary>
    /// Create a required directory.
    /// </summary>
    CreateDirectory = 2,
    /// <summary>
    /// Write verified target bytes, backing up any existing file first.
    /// </summary>
    WriteFile = 3
}

/// <summary>
/// Describes one authorized mutation without performing it.
/// </summary>
/// <remarks>
/// Initializes exact before/after fingerprints; directory operations never authorize recursive deletion.
/// </remarks>
/// <param name="Path">The relative destination path.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="ExpectedBefore">The original local bytes, or null for missing files and directories.</param>
/// <param name="ExpectedAfter">The target file bytes, or null for deletion and directory operations.</param>
public sealed record PlannedFileOperation(string Path, FileOperationKind Kind, GameFileContent? ExpectedBefore, GameFileContent? ExpectedAfter)
{
    /// <summary>
    /// Gets whether the transaction must finish and verify a backup before this mutation.
    /// </summary>
    public bool RequiresBackup => ExpectedBefore is not null;
}

/// <summary>
/// Describes whether an existing plan can safely proceed against a fresh snapshot.
/// </summary>
public enum FilePlanApplicability
{
    /// <summary>
    /// The original snapshot matches and mutations remain to be applied.
    /// </summary>
    Ready = 0,
    /// <summary>
    /// The complete expected result already matches; do not repeat mutations.
    /// </summary>
    AlreadyApplied = 1,
    /// <summary>
    /// The installation changed or only part of the plan was applied; replan or recover.
    /// </summary>
    Changed = 2,
    /// <summary>
    /// Unresolved conflicts prevent every mutation.
    /// </summary>
    Blocked = 3
}
