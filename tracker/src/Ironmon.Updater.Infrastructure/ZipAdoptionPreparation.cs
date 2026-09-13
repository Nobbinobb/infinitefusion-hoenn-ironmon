using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Owns prepared standard Git metadata while the original ZIP installation remains unchanged.
/// </summary>
public sealed class ZipAdoptionPreparation : IDisposable
{
    private readonly string _parent;
    private readonly string _workspace;
    private readonly IReadOnlyList<GameDirectoryEntry> _snapshot;

    /// <summary>
    /// Gets the recognized installation root.
    /// </summary>
    public string InstallationRoot { get; }

    /// <summary>
    /// Gets the complete directory to promote as .git in the later file transaction.
    /// </summary>
    public string MetadataDirectory { get; }

    /// <summary>
    /// Gets the historical commit represented by the prepared branch and index.
    /// </summary>
    public string BaselineCommit { get; }

    /// <summary>
    /// Gets the verified future target whose objects are retained in the prepared repository.
    /// </summary>
    public string TargetCommit { get; }

    /// <summary>
    /// Gets unrelated files that adoption must preserve.
    /// </summary>
    public IReadOnlyList<string> ExtraFiles { get; }

    /// <summary>
    /// Gets extra paths that obstruct target files and require a later conflict decision.
    /// </summary>
    public IReadOnlyList<string> TargetCollisions { get; }

    /// <summary>
    /// Creates a caller-owned preparation result; this type does not apply a file transaction.
    /// </summary>
    /// <param name="parent">The updater-owned preparation root.</param>
    /// <param name="workspace">The private workspace removed on disposal.</param>
    /// <param name="installationRoot">The recognized game root.</param>
    /// <param name="baselineCommit">The recognized historical commit.</param>
    /// <param name="targetCommit">The approved future target.</param>
    /// <param name="snapshot">The inspected installation entries.</param>
    /// <param name="extraFiles">The unrelated files to preserve.</param>
    /// <param name="targetCollisions">The obstructing extra paths.</param>
    internal ZipAdoptionPreparation(string parent, string workspace, string installationRoot, string baselineCommit, string targetCommit, IReadOnlyList<GameDirectoryEntry> snapshot, IEnumerable<string> extraFiles, IEnumerable<string> targetCollisions)
    {
        _parent = parent;
        _workspace = workspace;
        _snapshot = snapshot;
        InstallationRoot = installationRoot;
        MetadataDirectory = Path.Combine(workspace, GameDirectorySnapshot.GitDirectory);
        BaselineCommit = baselineCommit;
        TargetCommit = targetCommit;
        ExtraFiles = Array.AsReadOnly(extraFiles.ToArray());
        TargetCollisions = Array.AsReadOnly(targetCollisions.ToArray());
    }

    /// <summary>
    /// Rejects any changed installation entry before the later transaction takes ownership of metadata promotion.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel revalidation.</param>
    /// <returns>A task that completes only if the original installation snapshot still matches.</returns>
    public async Task RevalidateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = await GameDirectorySnapshot.ReadAsync(InstallationRoot, cancellationToken).ConfigureAwait(false);
        if (!_snapshot.SequenceEqual(current))
            throw new IOException(UpdaterText.ZipAdoptionPreparationTheZIPInstallationChangedAfterPreparationPrepareItAgain);
    }

    /// <summary>
    /// Removes only private preparation files; metadata already transferred to a transaction is outside this workspace.
    /// </summary>
    public void Dispose()
        => PlainPaths.DeleteOwned(_parent, _workspace);
}
