using Ironmon.Updater.Core;
using static Ironmon.Updater.Infrastructure.TransactionStorage;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Adds folder promotion for empty installations while retaining the ordinary file recovery protocol.
/// </summary>
public sealed partial class UpdateTransaction
{
    /// <summary>
    /// Restricts directory promotion to independently authorized plans that add files to an empty destination.
    /// </summary>
    /// <param name="description">The authenticated original snapshot.</param>
    /// <param name="operations">The reconstructed file operations.</param>
    /// <returns>Whether every program path starts absent and no existing Git tree is replaced.</returns>
    private static bool IsFreshInstallation(TransactionDescription description, IReadOnlyList<PlannedFileOperation> operations)
        => description.Plan.Local.Length == 0 && description.Plan.Baseline.Length == 0 && description.GitBefore is null && operations.All(operation => operation.ExpectedBefore is null && operation.Kind is FileOperationKind.CreateDirectory or FileOperationKind.WriteFile);

    /// <summary>
    /// Promotes complete top-level entries under one durable intent covering all possible file mutations.
    /// </summary>
    /// <remarks>
    /// The cursor remains the highest possibly applied file operation. Existing recovery engines can restore a partial batch by reconciling each file with its before and after state, including files whose promotion never happened.
    /// </remarks>
    /// <param name="description">The authenticated empty installation.</param>
    /// <param name="directory">The durable transaction owner.</param>
    /// <param name="operations">Every authorized new file and directory.</param>
    /// <param name="journal">The already flushed intent covering the complete batch.</param>
    /// <param name="cancellationToken">Cancellation routes through ordinary file rollback.</param>
    private async Task PromoteFreshAsync(TransactionDescription description, string directory, IReadOnlyList<PlannedFileOperation> operations, TransactionJournal journal, CancellationToken cancellationToken)
    {
        var payload = PlainPaths.Child(directory, PayloadDirectory);
        var groups = operations.GroupBy(operation => operation.Path.Split('/')[0], StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);
        var installed = 0;
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _ensureIdle(cancellationToken).ConfigureAwait(false);
            var source = PlainPaths.Child(payload, group.Key);
            var destination = PlainPaths.Child(description.InstallationRoot, group.Key);
            using var sourceAncestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(source) : null;
            using var destinationAncestors = OperatingSystem.IsWindows() ? WindowsUpdateAccess.HoldAncestors(destination) : null;
            if (Path.Exists(destination))
                throw new IOException(UpdaterText.UpdateTransactionAPlannedNewDirectoryIsUnexpectedlyOccupied);

            if (Directory.Exists(source))
            {
                Directory.Move(source, destination);
            }
            else
            {
                File.Move(source, destination);
            }

            Notify(journal, TransactionBoundary.Mutation);
            installed += group.Count();
            InstallationProgressScope.Report(new(InstallationStage.InstallingFiles, installed, operations.Count));
        }
    }
}
