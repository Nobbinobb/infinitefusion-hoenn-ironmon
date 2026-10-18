using Ironmon.Updater.Core;
using System.Buffers.Binary;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Identifies a live administrator session without granting new installation choices.
/// </summary>
/// <remarks>
/// Constructs a capability passed only through authenticated local process handoffs.
/// </remarks>
/// <param name="PipeName">The random administrator endpoint.</param>
/// <param name="ProcessId">The actual process returned by Windows elevation.</param>
/// <param name="Secret">The unguessable session capability, never persisted in the installation.</param>
public sealed record ProtectedUpdateTicket(string PipeName, int ProcessId, Guid Secret);

/// <summary>
/// Defines the closed set of operations on one reviewed administrator transaction.
/// </summary>
public enum ProtectedUpdateOperation
{
    /// <summary>
    /// Prepares the exact reviewed release after independent authentication.
    /// </summary>
    Prepare = 0,

    /// <summary>
    /// Opens a retained transaction for independent recovery.
    /// </summary>
    OpenRecovery = 1,

    /// <summary>
    /// Revalidates the bound transaction before the normal UI closes.
    /// </summary>
    Validate = 2,

    /// <summary>
    /// Applies the already prepared transaction.
    /// </summary>
    Apply = 3,

    /// <summary>
    /// Recovers the already selected transaction.
    /// </summary>
    Recover = 4,

    /// <summary>
    /// Discards an untouched preparation.
    /// </summary>
    Discard = 5,

    /// <summary>
    /// Saves bounded navigation for the original unelevated tracker.
    /// </summary>
    PreserveNavigation = 6,

    /// <summary>
    /// Downloads the optional library through the fixed sprite service after commit.
    /// </summary>
    Sprites = 7,

    /// <summary>
    /// Ends the administrator session.
    /// </summary>
    Close = 8,

    /// <summary>
    /// Opens an authenticated installed release for optional sprite work only.
    /// </summary>
    OpenInstalled = 9
}

/// <summary>
/// Carries bounded typed input rather than commands, source paths or executable arguments.
/// </summary>
/// <remarks>
/// Constructs initialization evidence or an operation on the previously bound transaction.
/// </remarks>
/// <param name="Ticket">The authenticated process session.</param>
/// <param name="Operation">The closed operation discriminator.</param>
/// <param name="Authorization">Signed release evidence and exact consent, only for initialization.</param>
/// <param name="Root">The originally reviewed installation root.</param>
/// <param name="TransactionId">The bound transaction, empty only before preparation.</param>
/// <param name="Ancestor">The existing ancestor captured before UAC.</param>
/// <param name="AncestorIdentity">The Windows filesystem identity captured before UAC.</param>
/// <param name="Navigation">Optional opaque navigation, never interpreted as a command.</param>
/// <param name="IncludeUnavailable">Whether optional sprite work retries previously unavailable sheets.</param>
/// <param name="ProgressEnabled">Whether the peer accepts intermediate numeric progress frames.</param>
internal sealed record ProtectedUpdateMessage(ProtectedUpdateTicket Ticket, ProtectedUpdateOperation Operation, IronmonUpdateAuthorization? Authorization, string Root, Guid TransactionId, string? Ancestor = null, string? AncestorIdentity = null, string? Navigation = null, bool IncludeUnavailable = false, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool ProgressEnabled = false);

/// <summary>
/// Reports only typed results or a bounded failure from the administrator worker.
/// </summary>
/// <remarks>
/// Constructs the completed operation result; no returned path is an execution authority.
/// </remarks>
/// <param name="Prepared">The authenticated preparation or recovery identity.</param>
/// <param name="Result">The durable transaction outcome.</param>
/// <param name="Error">The operation failure, if any.</param>
/// <param name="Cancelled">Whether safe cancellation completed.</param>
/// <param name="Sprites">The measured optional library outcome.</param>
/// <param name="Progress">An intermediate measurement, never a completed result.</param>
internal sealed record ProtectedUpdateReply(PreparedIronmonUpdate? Prepared = null, TransactionResult? Result = null, string? Error = null, bool Cancelled = false, ProtectedSpriteResult? Sprites = null, [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] InstallationProgress? Progress = null);

/// <summary>
/// Carries measured sprite results across the administrator boundary without a library dependency cycle.
/// </summary>
/// <remarks>
/// Constructs counts returned by the fixed shared sprite service.
/// </remarks>
/// <param name="Downloaded">The downloaded sheets.</param>
/// <param name="Unchanged">The current sheets reused.</param>
/// <param name="Failed">The retryable failed sheets.</param>
/// <param name="Bytes">The downloaded bytes.</param>
/// <param name="Unavailable">The unavailable remote sheets.</param>
public sealed record ProtectedSpriteResult(int Downloaded, int Unchanged, int Failed, long Bytes, int Unavailable);

/// <summary>
/// Implements bounded framing and operating-system peer identification for administrator messages.
/// </summary>
internal static partial class ProtectedUpdateProtocol
{
    internal const string Argument = "--administrator-session";
    internal const string PipePrefix = "Ironmon.Admin.";
    internal const string LocalServer = ".";
    private const string KernelLibrary = "kernel32.dll";
    private const int MaximumBytes = 64 * 1024 * 1024;

    /// <summary>
    /// Rejects malformed capabilities before connecting to any local endpoint.
    /// </summary>
    /// <param name="ticket">The candidate session capability.</param>
    internal static void Validate(ProtectedUpdateTicket ticket)
    {
        if (ticket.ProcessId <= 0 || ticket.Secret == Guid.Empty || !ticket.PipeName.StartsWith(PipePrefix, StringComparison.Ordinal) || !Guid.TryParseExact(ticket.PipeName[PipePrefix.Length..], TransactionStorage.GuidFormat, out _))
            throw new InvalidDataException(UpdaterText.ProtectedUpdateProtocolTheAdministratorSessionIdentityIsInvalid);
    }

    /// <summary>
    /// Writes one strict size-bounded message.
    /// </summary>
    /// <typeparam name="T">The closed message contract.</typeparam>
    /// <param name="stream">The authenticated pipe.</param>
    /// <param name="value">The message.</param>
    /// <param name="cancellationToken">The operation token.</param>
    /// <returns>The complete framed write.</returns>
    internal static async Task WriteAsync<T>(Stream stream, T value, CancellationToken cancellationToken)
    {
        var bytes = TransactionStorage.Serialize(value);
        if (bytes.Length > MaximumBytes)
            throw new InvalidDataException(UpdaterText.ProtectedUpdateProtocolTheAdministratorMessageExceedsItsSizeLimit);

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads one strict message without trusting the declared length.
    /// </summary>
    /// <typeparam name="T">The expected closed contract.</typeparam>
    /// <param name="stream">The connected pipe.</param>
    /// <param name="cancellationToken">The bounded read token.</param>
    /// <returns>The validated message.</returns>
    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var count = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (count <= 0 || count > MaximumBytes)
            throw new InvalidDataException(UpdaterText.ProtectedUpdateProtocolTheAdministratorMessageLengthIsInvalid);

        var bytes = new byte[count];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return TransactionStorage.Deserialize<T>(bytes);
    }

    /// <summary>
    /// Gets the actual server PID for a connected client.
    /// </summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <returns>The Windows-reported peer process.</returns>
    internal static uint ServerId(NamedPipeClientStream pipe)
        => GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var id) ? id : throw new IOException(UpdaterText.ProtectedUpdateProtocolTheAdministratorPipeOwnerCouldNotBeVerified);

    /// <summary>
    /// Gets the actual client PID for a connected server.
    /// </summary>
    /// <param name="pipe">The connected pipe.</param>
    /// <returns>The Windows-reported peer process.</returns>
    internal static uint ClientId(NamedPipeServerStream pipe)
        => GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var id) ? id : throw new IOException(UpdaterText.ProtectedUpdateProtocolTheAdministratorPipeClientCouldNotBeVerified);

    /// <summary>
    /// Reads the operating-system server identity.
    /// </summary>
    /// <param name="pipe">The pipe handle.</param>
    /// <param name="processId">The returned PID.</param>
    /// <returns>Whether the identity was available.</returns>
    [LibraryImport(KernelLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    /// <summary>
    /// Reads the operating-system client identity.
    /// </summary>
    /// <param name="pipe">The pipe handle.</param>
    /// <param name="processId">The returned PID.</param>
    /// <returns>Whether the identity was available.</returns>
    [LibraryImport(KernelLibrary, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
}
