using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Ironmon.Updater.Core;
using Microsoft.Win32.SafeHandles;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Carries bounded shutdown context without accepting arbitrary commands or executable arguments.
/// </summary>
/// <remarks>
/// Constructs the reviewed transaction handoff; navigation is opaque data interpreted only by the tracker after relaunch.
/// </remarks>
/// <param name="InstallationRoot">The bound installation root.</param>
/// <param name="TransactionId">The prepared transaction UUID.</param>
/// <param name="Tracker">The original tracker process identity.</param>
/// <param name="Game">The original game process identity, if running.</param>
/// <param name="Navigation">At most 4096 characters of tracker navigation context.</param>
/// <param name="Recover">Whether the independent helper must recover instead of applying a prepared update.</param>
/// <param name="ProtectedPreparation">The already authenticated administrator session, when protected writes are required.</param>
public sealed record UpdaterHandoffRequest(string InstallationRoot, Guid TransactionId, UpdateProcessIdentity Tracker, UpdateProcessIdentity? Game, string Navigation, bool Recover = false, PreparedIronmonUpdate? ProtectedPreparation = null);

/// <summary>
/// Keeps the tracker alive until an independently running verified helper acknowledges a bounded authenticated pipe handoff.
/// </summary>
public static partial class UpdaterHandoff
{
    /// <summary>
    /// Selects the acknowledged tracker handoff entry point.
    /// </summary>
    public const string HandoffArgument = "--handoff";
    /// <summary>
    /// Selects the independent recovery entry point.
    /// </summary>
    public const string RecoverArgument = "--recover";
    /// <summary>
    /// Fixes the only tracker executable the helper may close or relaunch.
    /// </summary>
    public const string TrackerRelativePath = "Ironmon Tracker/Ironmon Tracker.exe";
    /// <summary>
    /// Fixes the independent recovery executable name.
    /// </summary>
    public const string HelperFileName = "Ironmon.Updater.exe";
    private const string RecoveryDirectory = "recovery/1";
    private const string PipePrefix = "Ironmon.Update.";
    private const string LocalServer = ".";
    private const string KernelLibrary = "kernel32.dll";
    private const byte Ready = 1;
    private const byte Proceed = 2;
    private const int MaximumMessageBytes = 16384;
    private const int MaximumNavigationCharacters = 4096;
    private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Verifies the fixed out-of-tree helper against authenticated release content, then performs the acknowledgement-before-close protocol.
    /// </summary>
    /// <remarks>
    /// Expected helper content must come from authenticated release metadata. This method never requests elevation or accepts a helper pathname from a journal.
    /// </remarks>
    /// <param name="request">The bound process and transaction context.</param>
    /// <param name="expectedHelper">The independently authenticated helper content identity.</param>
    /// <param name="quiesce">Stops and awaits polling, listeners and sprite work after acknowledgement.</param>
    /// <param name="resume">Restores tracker services if handoff fails before tracker shutdown.</param>
    /// <param name="closeTracker">Requests normal tracker application shutdown.</param>
    /// <param name="helperVersion">The authenticated numeric helper version, or null for the original recovery helper.</param>
    /// <param name="cancellationToken">Cancels preparation and handoff before shutdown.</param>
    /// <returns>The independently running helper identity.</returns>
    public static async Task<UpdateProcessIdentity> LaunchAsync(UpdaterHandoffRequest request, GameFileContent expectedHelper, Func<CancellationToken, Task> quiesce, Func<Task> resume, Func<Task> closeTracker, string? helperVersion = null, CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var current = Process.GetCurrentProcess();
        if (request.Tracker != UpdateProcessIdentity.Capture(current))
            throw new InvalidDataException(UpdaterText.UpdaterHandoffOnlyTheTrackerProcessNamedInTheHandoffCan);

        if (helperVersion is not null)
            ReleaseProtocol.ParseVersion(helperVersion);

        var recoveryDirectory = helperVersion is null ? RecoveryDirectory : RecoveryHelperPackage.DirectoryName + '/' + helperVersion;
        var helper = PlainPaths.Child(request.InstallationRoot, InstallationLease.StateDirectory + '/' + recoveryDirectory + '/' + HelperFileName);
        if (await TransactionStorage.ContentAsync(helper, cancellationToken).ConfigureAwait(false) != expectedHelper)
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheIndependentHelperFailedAuthenticatedContentVerification);

        var pipeName = PipePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper)! };
        start.ArgumentList.Add(HandoffArgument);
        start.ArgumentList.Add(pipeName);
        using var child = Process.Start(start) ?? throw new IOException(UpdaterText.UpdaterHandoffTheIndependentUpdaterCouldNotStart);
        var childIdentity = UpdateProcessIdentity.Capture(child);
        var quiesced = false;
        try
        {
            await pipe.WaitForConnectionAsync(timeout.Token).ConfigureAwait(false);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var clientId) || clientId != childIdentity.Id)
                throw new IOException(UpdaterText.UpdaterHandoffAnUnexpectedProcessConnectedToTheUpdaterHandoff);

            await WriteMessageAsync(pipe, request, timeout.Token).ConfigureAwait(false);
            await ReadSignalAsync(pipe, Ready, timeout.Token).ConfigureAwait(false);
            using var acknowledged = childIdentity.Open() ?? throw new IOException(UpdaterText.UpdaterHandoffTheAcknowledgedUpdaterAlreadyExited);
            quiesced = true;
            await quiesce(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            await pipe.WriteAsync(new byte[] { Proceed }, timeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
            await closeTracker().ConfigureAwait(false);
            return childIdentity;
        }
        catch
        {
            if (quiesced)
                await resume().ConfigureAwait(false);

            throw;
        }
    }

    /// <summary>
    /// Receives context from the actual pipe-server process and acknowledges only after independent transaction validation.
    /// </summary>
    /// <param name="pipeName">The bounded randomly generated pipe name.</param>
    /// <param name="validate">Authenticates the descriptor and checks helper readiness before tracker shutdown.</param>
    /// <param name="cancellationToken">The handoff cancellation token.</param>
    /// <returns>The validated request only after the tracker authorizes shutdown continuation.</returns>
    public static async Task<UpdaterHandoffRequest> AcceptAsync(string pipeName, Func<UpdaterHandoffRequest, CancellationToken, Task> validate, CancellationToken cancellationToken = default)
    {
        if (!pipeName.StartsWith(PipePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(pipeName[PipePrefix.Length..], TransactionStorage.GuidFormat, out _))
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheUpdaterHandoffPipeNameIsInvalid);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        await using var pipe = new NamedPipeClientStream(LocalServer, pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        var request = await ReadMessageAsync(pipe, timeout.Token).ConfigureAwait(false);
        ValidateRequest(request);
        if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverId) || serverId != request.Tracker.Id)
            throw new IOException(UpdaterText.UpdaterHandoffTheHandoffTrackerDoesNotOwnTheConnectedPipe);

        using var tracker = request.Tracker.Open() ?? throw new IOException(UpdaterText.UpdaterHandoffTheTrackerExitedBeforeTheHelperAcknowledgedReadiness);
        await validate(request, timeout.Token).ConfigureAwait(false);
        await pipe.WriteAsync(new byte[] { Ready }, timeout.Token).ConfigureAwait(false);
        await pipe.FlushAsync(timeout.Token).ConfigureAwait(false);
        await ReadSignalAsync(pipe, Proceed, timeout.Token).ConfigureAwait(false);
        return request;
    }

    /// <summary>
    /// Bounds context and fixes shutdown targets to this installation's known executables.
    /// </summary>
    /// <param name="request">The proposed handoff.</param>
    private static void ValidateRequest(UpdaterHandoffRequest request)
    {
        var root = PlainPaths.Full(request.InstallationRoot);
        if (request.ProtectedPreparation is { } prepared)
        {
            if (prepared.InstallationRoot != root || prepared.TransactionId != request.TransactionId || prepared.Elevation is null)
                throw new InvalidDataException(UpdaterText.UpdaterHandoffTheAdministratorHandoffBelongsToAnotherTransaction);

            ProtectedUpdateProtocol.Validate(prepared.Elevation);
        }

        if (request.TransactionId == Guid.Empty || request.Navigation is null || request.Navigation.Length > MaximumNavigationCharacters || request.Tracker.Id <= 0 || request.Tracker.StartedUtcTicks <= 0 || !request.Tracker.ExecutablePath.Equals(PlainPaths.Child(root, TrackerRelativePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheTrackerHandoffContextIsInvalidOrTooLarge);

        if (request.Game is not null && (request.Game.Id <= 0 || request.Game.StartedUtcTicks <= 0 || !request.Game.ExecutablePath.Equals(PlainPaths.Child(root, GameInstallationLocator.GameExecutable), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheGameShutdownTargetIsOutsideThisInstallation);
    }

    /// <summary>
    /// Sends a bounded length-prefixed context message.
    /// </summary>
    /// <param name="stream">The authenticated connected pipe.</param>
    /// <param name="request">The context to serialize.</param>
    /// <param name="cancellationToken">The bounded timeout token.</param>
    private static async Task WriteMessageAsync(Stream stream, UpdaterHandoffRequest request, CancellationToken cancellationToken)
    {
        var bytes = TransactionStorage.Serialize(request);
        if (bytes.Length > MaximumMessageBytes)
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheUpdaterHandoffIsTooLarge);

        var length = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        await stream.WriteAsync(length, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one bounded complete handoff without unbounded line buffering.
    /// </summary>
    /// <param name="stream">The connected pipe.</param>
    /// <param name="cancellationToken">The bounded timeout token.</param>
    /// <returns>The strictly parsed request.</returns>
    private static async Task<UpdaterHandoffRequest> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(length, cancellationToken).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(length);
        if (count <= 0 || count > MaximumMessageBytes)
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheUpdaterHandoffLengthIsInvalid);

        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return TransactionStorage.Deserialize<UpdaterHandoffRequest>(bytes);
    }

    /// <summary>
    /// Requires the next protocol byte to be the expected acknowledgement or continuation.
    /// </summary>
    /// <param name="stream">The connected pipe.</param>
    /// <param name="expected">The required signal.</param>
    /// <param name="cancellationToken">The bounded timeout token.</param>
    private static async Task ReadSignalAsync(Stream stream, byte expected, CancellationToken cancellationToken)
    {
        var signal = new byte[1];
        await stream.ReadExactlyAsync(signal, cancellationToken).ConfigureAwait(false);
        if (signal[0] != expected)
            throw new InvalidDataException(UpdaterText.UpdaterHandoffTheUpdaterHandoffSignalIsInvalid);
    }

    /// <summary>
    /// Obtains the actual client PID from Windows instead of trusting message contents.
    /// </summary>
    /// <param name="pipe">The connected server handle.</param>
    /// <param name="processId">The actual client PID.</param>
    /// <returns>Whether Windows returned the peer identity.</returns>
    [LibraryImport(KernelLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);

    /// <summary>
    /// Obtains the actual server PID from Windows instead of trusting message contents.
    /// </summary>
    /// <param name="pipe">The connected client handle.</param>
    /// <param name="processId">The actual server PID.</param>
    /// <returns>Whether Windows returned the peer identity.</returns>
    [LibraryImport(KernelLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
