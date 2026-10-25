using Ironmon.Updater.Core;
namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Excludes cooperating program and sprite writers across processes for one installation.
/// </summary>
public sealed class InstallationLease : IDisposable
{
    internal const string StateDirectory = ".ironmon-update";
    internal const string ActiveFile = "active.json";
    private const string LockFile = "installation.lock";
    private readonly FileStream _stream;

    /// <summary>
    /// Gets the canonical installation protected by the open operating-system lease.
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Retains the exclusive handle until the complete operation or recovery finishes.
    /// </summary>
    /// <param name="root">The checked installation path.</param>
    /// <param name="stream">The exclusive lock handle.</param>
    private InstallationLease(string root, FileStream stream)
    {
        Root = root;
        _stream = stream;
    }

    /// <summary>
    /// Acquires a lease immediately; contention and unfinished updates are explicit blockers rather than silent waits.
    /// </summary>
    /// <param name="installationRoot">The existing game directory.</param>
    /// <param name="recovery">Whether the transaction engine will validate and handle an active journal itself.</param>
    /// <returns>A lease released automatically if its process exits.</returns>
    public static InstallationLease Acquire(string installationRoot, bool recovery = false)
    {
        var root = PlainPaths.Full(installationRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(UpdaterText.InstallationLeaseTheInstallationDirectoryNoLongerExists);

        var state = PlainPaths.Child(root, StateDirectory);
        Directory.CreateDirectory(state);
        var stream = new FileStream(PlainPaths.Child(state, LockFile), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try
        {
            if (!recovery && Path.Exists(PlainPaths.Child(state, ActiveFile)))
                throw new IOException(UpdaterText.InstallationLeaseAnUnfinishedUpdateMustBeRecoveredBeforeAnotherInstallation);

            return new InstallationLease(root, stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases the cross-process lease without deleting the shared lock pathname.
    /// </summary>
    public void Dispose()
        => _stream.Dispose();
}
