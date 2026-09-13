using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Identifies durable transaction states independently of presentation and process lifetime.
/// </summary>
public enum TransactionPhase
{
    /// <summary>
    /// All backups and staged bytes are verified; installed files remain unchanged.
    /// </summary>
    Prepared = 0,
    /// <summary>
    /// Mutations may have started and require completion or rollback.
    /// </summary>
    Applying = 1,
    /// <summary>
    /// Recovery is restoring the original installation.
    /// </summary>
    RollingBack = 2,
    /// <summary>
    /// Program, metadata and component checks all passed.
    /// </summary>
    Committed = 3,
    /// <summary>
    /// The complete original snapshot has been restored and verified.
    /// </summary>
    RolledBack = 4,
    /// <summary>
    /// Recovery could not complete; retain every backup and block new writers.
    /// </summary>
    RecoveryRequired = 5
}

/// <summary>
/// Carries immutable transaction authority separately from mutable journal progress.
/// </summary>
/// <remarks>
/// Constructs an envelope whose exact serialized bytes must be authorized independently on preparation, application and recovery.
/// </remarks>
/// <param name="SchemaVersion">The supported descriptor schema.</param>
/// <param name="EngineVersion">The recovery engine protocol, independent of tracker versions.</param>
/// <param name="InstallationId">The stable installation identity.</param>
/// <param name="RootIdentity">The filesystem identity captured for the installation directory.</param>
/// <param name="TransactionId">The unique transaction identifier.</param>
/// <param name="InstallationRoot">The canonical destination bound to authorization.</param>
/// <param name="Plan">The complete reviewed file plan inputs.</param>
/// <param name="GitBefore">The complete original Git tree, or null for a ZIP installation.</param>
/// <param name="GitAfter">The prepared complete Git tree, or null when Git is not participating.</param>
public sealed record TransactionDescription(int SchemaVersion, int EngineVersion, Guid InstallationId, string RootIdentity, Guid TransactionId, string InstallationRoot, FilePlanSpecification Plan, LocalFileEntry[]? GitBefore, LocalFileEntry[]? GitAfter);

/// <summary>
/// Records durable intent and completion without granting permission to modify arbitrary paths.
/// </summary>
/// <remarks>
/// Constructs a progress generation. Cursor is the highest operation with persisted intent, even during rollback.
/// </remarks>
/// <param name="SchemaVersion">The journal schema.</param>
/// <param name="Generation">The increasing durable generation.</param>
/// <param name="DescriptionHash">The immutable authorized descriptor digest.</param>
/// <param name="Phase">The recovery state.</param>
/// <param name="Cursor">The last operation whose intent was saved, or minus one.</param>
/// <param name="Error">The last failure suitable for a recovery display.</param>
public sealed record TransactionJournal(int SchemaVersion, long Generation, string DescriptionHash, TransactionPhase Phase, int Cursor, string? Error);

/// <summary>
/// Reports installation outcome separately from the optional tracker relaunch outcome.
/// </summary>
/// <remarks>
/// Constructs a result; only Committed represents installation success.
/// </remarks>
/// <param name="Phase">The final durable installation phase.</param>
/// <param name="Error">An installation or recovery failure.</param>
/// <param name="RelaunchError">A relaunch failure that does not undo a committed installation.</param>
public sealed record TransactionResult(TransactionPhase Phase, string? Error = null, string? RelaunchError = null);

/// <summary>
/// Describes a durable boundary for progress reporting and process-termination fixtures.
/// </summary>
/// <remarks>
/// Constructs a notification after the named boundary has actually been reached.
/// </remarks>
/// <param name="Phase">The current transaction phase.</param>
/// <param name="Operation">The zero-based mutation index.</param>
/// <param name="Boundary">The completed durability boundary.</param>
public sealed record TransactionProgress(TransactionPhase Phase, int Operation, TransactionBoundary Boundary);

/// <summary>
/// Identifies observable journal and mutation boundaries.
/// </summary>
public enum TransactionBoundary
{
    /// <summary>
    /// Intent is flushed before mutation.
    /// </summary>
    Intent = 0,
    /// <summary>
    /// A filesystem mutation finished before its completion was journaled.
    /// </summary>
    Mutation = 1,
    /// <summary>
    /// Completion is durably recorded.
    /// </summary>
    Completion = 2,
    /// <summary>
    /// Original Git metadata was moved aside before prepared metadata promotion.
    /// </summary>
    GitDetached = 3
}

/// <summary>
/// Supplies trusted release and installation authorization independently of user-writable journals.
/// </summary>
public interface ITransactionAuthority
{
    /// <summary>
    /// Authenticates the exact descriptor and installation destination, including consent and any elevated context.
    /// </summary>
    /// <remarks>
    /// Implementations must fail closed without authenticated release inventories and trusted local authorization. A digest read from the same transaction directory is not an authority.
    /// </remarks>
    /// <param name="description">The reconstructed proposed authority.</param>
    /// <param name="exactBytes">The exact persisted descriptor bytes.</param>
    /// <param name="cancellationToken">The preparation or recovery cancellation token.</param>
    /// <returns>A task that fails if the authority is missing or invalid.</returns>
    Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Verifies authenticated component versions, installed manifest and expected Git references before commit.
    /// </summary>
    /// <param name="description">The independently authenticated transaction.</param>
    /// <param name="cancellationToken">The verification cancellation token.</param>
    /// <returns>A task that fails on any incompatible or incomplete component set.</returns>
    Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken);
}
