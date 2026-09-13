using Ironmon.Updater.Core;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Identifies work measured within one installation stage, never an estimated overall percentage.
/// </summary>
public enum InstallationStage
{
    /// <summary>
    /// Prepares the private download tool.
    /// </summary>
    PreparingDownload = 0,
    /// <summary>
    /// Receives game objects from the upstream repository.
    /// </summary>
    DownloadingGame = 1,
    /// <summary>
    /// Resolves downloaded game objects.
    /// </summary>
    ProcessingGame = 2,
    /// <summary>
    /// Checks the approved game revision and repository.
    /// </summary>
    VerifyingGame = 3,
    /// <summary>
    /// Creates the local game archive.
    /// </summary>
    PreparingGame = 4,
    /// <summary>
    /// Extracts and verifies signed files.
    /// </summary>
    ExtractingFiles = 5,
    /// <summary>
    /// Prepares recovery evidence and backups.
    /// </summary>
    PreparingRecovery = 6,
    /// <summary>
    /// Verifies the complete prepared installation.
    /// </summary>
    VerifyingFiles = 7,
    /// <summary>
    /// Applies verified installation files.
    /// </summary>
    InstallingFiles = 8,
    /// <summary>
    /// Restores the previous installation.
    /// </summary>
    RestoringFiles = 9,
    /// <summary>
    /// Downloads the optional sprite library.
    /// </summary>
    DownloadingSprites = 10,
    /// <summary>
    /// Downloads an authenticated release package.
    /// </summary>
    DownloadingPackage = 11,
    /// <summary>
    /// Waits for running applications while retaining prepared installation files.
    /// </summary>
    WaitingForApplications = 12
}

/// <summary>
/// Carries bounded numeric progress without paths, Git output or execution authority.
/// </summary>
/// <remarks>
/// Constructs a measurement for one stage; a zero total means its full size is unknown.
/// </remarks>
/// <param name="Stage">The current operation.</param>
/// <param name="Completed">The completed units in this stage.</param>
/// <param name="Total">The known stage units, or zero.</param>
/// <param name="Bytes">The bytes received during a download.</param>
public sealed record InstallationProgress(InstallationStage Stage, long Completed = 0, long Total = 0, long Bytes = 0)
{
    /// <summary>
    /// Gets the localized stage and its measured counts.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string Text
    {
        get
        {
            var label = Stage switch
            {
                InstallationStage.PreparingDownload => UpdaterText.ProgressPreparingDownload,
                InstallationStage.DownloadingGame => UpdaterText.ProgressDownloadingGame,
                InstallationStage.ProcessingGame => UpdaterText.ProgressProcessingGame,
                InstallationStage.VerifyingGame => UpdaterText.ProgressVerifyingGame,
                InstallationStage.PreparingGame => UpdaterText.ProgressPreparingGame,
                InstallationStage.ExtractingFiles => UpdaterText.ProgressExtractingFiles,
                InstallationStage.PreparingRecovery => UpdaterText.ProgressPreparingRecovery,
                InstallationStage.VerifyingFiles => UpdaterText.ProgressVerifyingFiles,
                InstallationStage.InstallingFiles => UpdaterText.ProgressInstallingFiles,
                InstallationStage.RestoringFiles => UpdaterText.ProgressRestoringFiles,
                InstallationStage.DownloadingSprites => UpdaterText.ProgressDownloadingSprites,
                InstallationStage.WaitingForApplications => UpdaterText.ProgressWaitingForApplications,
                _ => UpdaterText.ProgressDownloadingPackage
            };
            if (Total > 0 && Stage != InstallationStage.DownloadingPackage)
            {
                label += UpdaterText.ProgressCounts(Completed, Total);
            }
            else if (Total == 0 && Completed > 0)
            {
                label += UpdaterText.ProgressProcessed(Completed);
            }

            if (Stage == InstallationStage.DownloadingPackage && Total > 0)
            {
                label += UpdaterText.ProgressPackageBytes(Completed / 1048576d, Total / 1048576d);
            }
            else if (Bytes > 0)
            {
                label += UpdaterText.ProgressReceived(Bytes / 1048576d);
            }

            return label;
        }
    }
}

/// <summary>
/// Routes measurements through one asynchronous operation without global event subscriptions or UI dependencies.
/// </summary>
public sealed class InstallationProgressScope : IDisposable
{
    private static readonly AsyncLocal<InstallationProgressScope?> _current = new();
    private readonly InstallationProgressScope? _previous;
    private readonly Action<InstallationProgress> _observer;
    private readonly Lock _gate = new();
    private InstallationProgress? _last;
    private long _reportedAt;

    /// <summary>
    /// Attaches an observer only to work flowing from the current execution context.
    /// </summary>
    /// <param name="observer">The synchronous bounded notification consumer.</param>
    public InstallationProgressScope(Action<InstallationProgress> observer)
    {
        _observer = observer;
        _previous = _current.Value;
        _current.Value = this;
    }

    /// <summary>
    /// Reports stage changes immediately and limits intermediate measurements to ten per second.
    /// </summary>
    /// <param name="value">The latest measurement.</param>
    public static void Report(InstallationProgress value)
        => Report(_current.Value, value);

    /// <summary>
    /// Captures the current observer for coordinated companion work with its own progress scope.
    /// </summary>
    /// <returns>A reporter bound to the originating operation rather than the caller's execution context.</returns>
    internal static Action<InstallationProgress> CaptureReporter()
    {
        var scope = _current.Value;
        return value => Report(scope, value);
    }

    /// <summary>
    /// Serializes bounded measurements through a specific captured observer.
    /// </summary>
    /// <param name="scope">The originating operation, or null when no observer is attached.</param>
    /// <param name="value">The measured progress.</param>
    private static void Report(InstallationProgressScope? scope, InstallationProgress value)
    {
        if (!Enum.IsDefined(value.Stage) || value.Completed < 0 || value.Total < 0 || value.Bytes < 0 || value.Total > 0 && value.Completed > value.Total)
            return;

        if (scope is null)
            return;

        lock (scope._gate)
        {
            var now = Environment.TickCount64;
            if (value == scope._last || (value.Stage == scope._last?.Stage && value.Total == scope._last.Total && value.Completed != 0 && value.Completed != value.Total && now - scope._reportedAt < 100))
                return;

            scope._last = value;
            scope._reportedAt = now;
            scope._observer(value);
        }
    }

    /// <summary>
    /// Restores the enclosing operation's observer.
    /// </summary>
    public void Dispose()
        => _current.Value = _previous;
}
