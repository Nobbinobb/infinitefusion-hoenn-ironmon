using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Cancels and drains installation work before handoff while rejecting new work until the tracker resumes.
/// </summary>
public sealed class InstallationWriterCoordinator
{
    private readonly Lock _gate = new();
    private readonly HashSet<string> _paused = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActiveWriter> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Runs one owned writer per installation and exposes its exact completion to shutdown coordination.
    /// </summary>
    /// <typeparam name="T">The writer result.</typeparam>
    /// <param name="installationRoot">The writer's installation.</param>
    /// <param name="write">The cancellation-aware operation including cleanup and final metadata writes.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The completed operation result.</returns>
    public async Task<T> RunAsync<T>(string installationRoot, Func<CancellationToken, Task<T>> write, CancellationToken cancellationToken = default)
    {
        var root = PlainPaths.Full(installationRoot);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writer = new ActiveWriter(cancellation, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        lock (_gate)
        {
            if (_paused.Contains(root) || !_active.TryAdd(root, writer))
                throw new InvalidOperationException(UpdaterText.InstallationWriterCoordinatorInstallationWorkIsAlreadyRunningOrPausedForAn);
        }

        try
        {
            return await write(cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                _active.Remove(root);
                writer.Finished.TrySetResult();
            }
        }
    }

    /// <summary>
    /// Rejects new writes, requests cancellation and awaits the active writer's complete cleanup.
    /// </summary>
    /// <param name="installationRoot">The installation being handed off.</param>
    /// <param name="cancellationToken">Cancels waiting while keeping new work paused until an explicit resume.</param>
    /// <returns>A task completing only after the owned writer has stopped.</returns>
    public async Task PauseAsync(string installationRoot, CancellationToken cancellationToken = default)
    {
        var root = PlainPaths.Full(installationRoot);
        ActiveWriter? writer;
        Task cancellation;
        lock (_gate)
        {
            _paused.Add(root);
            _active.TryGetValue(root, out writer);
            cancellation = writer?.Cancellation.CancelAsync() ?? Task.CompletedTask;
        }

        await cancellation.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (writer is not null)
            await writer.Finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Allows new installation work after a cancelled or failed tracker handoff.
    /// </summary>
    /// <param name="installationRoot">The paused installation.</param>
    public void Resume(string installationRoot)
    {
        var root = PlainPaths.Full(installationRoot);
        lock (_gate)
            _paused.Remove(root);
    }

    /// <summary>
    /// Owns cancellation and completion for the exact writer being drained.
    /// </summary>
    /// <remarks>
    /// Constructs one registered writer; cancellation lifetime belongs to RunAsync.
    /// </remarks>
    /// <param name="Cancellation">The linked writer cancellation.</param>
    /// <param name="Finished">The signal set after all cleanup finishes.</param>
    private sealed record ActiveWriter(CancellationTokenSource Cancellation, TaskCompletionSource Finished);
}
