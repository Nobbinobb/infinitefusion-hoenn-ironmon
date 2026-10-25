using Ironmon.Updater.Core;
using System.Diagnostics;
using System.Security.Principal;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Persists bounded navigation context and starts only the fixed installed tracker as the original unelevated user.
/// </summary>
public static class TrackerRelaunch
{
    private const string ResumeFile = "relaunch.json";
    private const string ResumeArgument = "--resume-update";

    /// <summary>
    /// Records navigation before tracker exit so successful installation does not lose its prior view context.
    /// </summary>
    /// <param name="request">The independently validated handoff.</param>
    public static void Preserve(UpdaterHandoffRequest request)
        => PreserveNavigation(request.InstallationRoot, request.TransactionId, request.Navigation);

    /// <summary>
    /// Writes only bounded opaque context into the selected installation's fixed resume record.
    /// </summary>
    /// <param name="root">The authenticated installation.</param>
    /// <param name="id">The bound transaction.</param>
    /// <param name="navigation">The opaque tracker context.</param>
    internal static void PreserveNavigation(string root, Guid id, string navigation)
    {
        if (navigation.Length > 4096)
            throw new InvalidDataException(UpdaterText.TrackerRelaunchTheTrackerNavigationContextIsTooLarge);

        var path = PlainPaths.Child(root, InstallationLease.StateDirectory + '/' + ResumeFile);
        TransactionStorage.WriteDurable(path, TransactionStorage.Serialize(new ResumeContext(1, id, navigation)));
    }

    /// <summary>
    /// Starts the fixed tracker binary with an opaque resume identifier, refusing an accidentally elevated game session.
    /// </summary>
    /// <param name="installationRoot">The verified completed installation.</param>
    /// <param name="transactionId">The matching resume context identifier.</param>
    /// <returns>A task that reports launch failure separately from installation success.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static Task StartAsync(string installationRoot, Guid transactionId)
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException(UpdaterText.TrackerRelaunchTheUpdateFinishedOpenTheTrackerNormallyFromYour);

        var executable = PlainPaths.Child(installationRoot, UpdaterHandoff.TrackerRelativePath);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add(ResumeArgument);
        start.ArgumentList.Add(transactionId.ToString(TransactionStorage.GuidFormat));
        using var process = Process.Start(start) ?? throw new IOException(UpdaterText.TrackerRelaunchTheUpdateFinishedButTheTrackerCouldNotStart);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads navigation only for the expected transaction; the tracker still validates its own route before navigation.
    /// </summary>
    /// <param name="installationRoot">The installed tracker root.</param>
    /// <param name="transactionId">The requested resume identity.</param>
    /// <returns>The bounded opaque navigation state.</returns>
    public static string ReadNavigation(string installationRoot, Guid transactionId)
    {
        var path = PlainPaths.Child(installationRoot, InstallationLease.StateDirectory + '/' + ResumeFile);
        var context = TransactionStorage.Deserialize<ResumeContext>(TransactionStorage.ReadBytes(path));
        if (context.SchemaVersion != 1 || context.TransactionId != transactionId || context.Navigation.Length > 4096)
            throw new InvalidDataException(UpdaterText.TrackerRelaunchTheSavedTrackerNavigationDoesNotMatchThisUpdate);

        return context.Navigation;
    }

    /// <summary>
    /// Stores navigation separately from immutable transaction authority and installed program files.
    /// </summary>
    /// <remarks>
    /// Constructs the bounded tracker resume record.
    /// </remarks>
    /// <param name="SchemaVersion">The resume schema.</param>
    /// <param name="TransactionId">The update that owns this navigation.</param>
    /// <param name="Navigation">The opaque tracker view state.</param>
    private sealed record ResumeContext(int SchemaVersion, Guid TransactionId, string Navigation);
}
