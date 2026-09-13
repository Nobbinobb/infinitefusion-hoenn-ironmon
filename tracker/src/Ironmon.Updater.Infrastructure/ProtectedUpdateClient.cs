using Ironmon.Updater.Core;
using System.IO.Pipes;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Starts one authenticated administrator worker while the tracker or Setup remains unelevated.
/// </summary>
/// <remarks>
/// Constructs the client from embedded release trust and the normal verified download cache.
/// </remarks>
/// <param name="verifier">The independently configured release verifier.</param>
/// <param name="downloads">The verified user download cache.</param>
public sealed class ProtectedUpdateClient(ReleaseVerifier verifier, ReleaseDownloadStore downloads)
{
    private const string StagingDirectory = "administrator-tools";
    private readonly ReleaseVerifier _verifier = verifier;
    private readonly ReleaseDownloadStore _downloads = downloads;

    /// <summary>
    /// Checks permissions only when an installation operation has been selected.
    /// </summary>
    /// <param name="root">The reviewed installation root.</param>
    /// <returns>Whether Windows requires administrator writes.</returns>
    public static bool RequiresElevation(string root)
        => OperatingSystem.IsWindows() && WindowsUpdateAccess.RequiresElevation(root);

    /// <summary>
    /// Authenticates the helper and starts privileged preparation for exactly the reviewed selection.
    /// </summary>
    /// <param name="authorization">The signed release and exact reviewed replacement consent.</param>
    /// <param name="cancellationToken">The preparation token.</param>
    /// <returns>The prepared transaction and live administrator capability.</returns>
    public Task<PreparedIronmonUpdate> PrepareAsync(IronmonUpdateAuthorization authorization, CancellationToken cancellationToken = default)
        => StartAsync(authorization, Guid.Empty, false, cancellationToken);

    /// <summary>
    /// Starts independently authenticated recovery from fixed retained release evidence.
    /// </summary>
    /// <param name="root">The affected installation.</param>
    /// <param name="id">The selected durable transaction.</param>
    /// <param name="cancellationToken">The recovery preparation token.</param>
    /// <returns>The bound recovery worker.</returns>
    public Task<PreparedIronmonUpdate> OpenRecoveryAsync(string root, Guid id, CancellationToken cancellationToken = default)
    {
        var authorization = ReleaseJson.Parse<IronmonUpdateAuthorization>(TransactionStorage.ReadBytes(IronmonOnlyUpdate.AuthorizationPath(root, id)), 64 * ReleaseJson.ManifestLimit);
        if (!PlainPaths.Full(authorization.Request.InstallationRoot).Equals(PlainPaths.Full(root), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheRecoveryEvidenceBelongsToAnotherInstallation);

        return StartAsync(authorization, id, false, cancellationToken);
    }

    /// <summary>
    /// Reauthenticates the installed release before requesting optional protected sprite writes.
    /// </summary>
    /// <param name="root">The installed game root.</param>
    /// <param name="cancellationToken">The optional-work token.</param>
    /// <returns>A session which cannot apply or recover program files.</returns>
    public Task<PreparedIronmonUpdate> OpenInstalledAsync(string root, CancellationToken cancellationToken = default)
    {
        var receipt = ReleaseJson.Parse<IronmonInstalledReceipt>(TransactionStorage.ReadBytes(PlainPaths.Child(root, IronmonOnlyUpdate.ReceiptPath)), ReleaseJson.ManifestLimit);
        var directory = PlainPaths.Child(root, IronmonOnlyUpdate.EvidenceDirectory);
        foreach (var path in Directory.EnumerateFiles(directory).Take(1024))
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), TransactionStorage.GuidFormat, out var id))
                continue;

            var authorization = ReleaseJson.Parse<IronmonUpdateAuthorization>(TransactionStorage.ReadBytes(IronmonOnlyUpdate.AuthorizationPath(root, id)), 64 * ReleaseJson.ManifestLimit);
            if (TransactionStorage.Hash(authorization.Target.Manifest) != receipt.ManifestSha256)
                continue;

            if (PlainPaths.Full(authorization.Request.InstallationRoot) != PlainPaths.Full(root))
                throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheInstalledReleaseRecordBelongsToAnotherFolder);

            return StartAsync(authorization, Guid.Empty, true, cancellationToken);
        }

        throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheInstalledReleaseVerificationRecordsAreMissingRepairThis);
    }

    /// <summary>
    /// Locks authenticated executable bytes and ancestry across the Windows elevation prompt.
    /// </summary>
    /// <param name="authorization">The complete reviewed authority.</param>
    /// <param name="id">An existing recovery transaction, or empty for preparation.</param>
    /// <param name="installed">Whether only optional installed-release work is requested.</param>
    /// <param name="cancellationToken">The bounded operation token.</param>
    /// <returns>The independently verified preparation result.</returns>
    private async Task<PreparedIronmonUpdate> StartAsync(IronmonUpdateAuthorization authorization, Guid id, bool installed, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(UpdaterText.ProtectedUpdateClientAdministratorInstallationIsSupportedOnWindowsOnly);

        var manifest = _verifier.Verify(authorization.Target.Manifest, authorization.Target.Signatures);
        var root = PlainPaths.Full(authorization.Request.InstallationRoot);
        var ancestor = root;
        while (!Directory.Exists(ancestor))
            ancestor = Path.GetDirectoryName(ancestor) ?? throw new IOException(UpdaterText.ProtectedUpdateClientTheInstallationParentNoLongerExists);

        var identity = InstallationIdentity.Read(ancestor);
        var staging = PlainPaths.Child(_downloads.Root, StagingDirectory + '/' + Guid.NewGuid().ToString(TransactionStorage.GuidFormat));
        var helper = await StageHelperAsync(authorization, manifest, staging, id != Guid.Empty || installed, cancellationToken).ConfigureAwait(false);
        var executable = PlainPaths.Child(staging, InstallationLease.StateDirectory + '/' + RecoveryHelperPackage.DirectoryName + '/' + helper.Version + '/' + UpdaterHandoff.HelperFileName);
        var handles = WindowsUpdateAccess.LockAncestors(executable);
        try
        {
            await using var locked = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (await TransactionStorage.ContentAsync(executable, cancellationToken).ConfigureAwait(false) != helper.Content)
                throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperChangedBeforeLaunch);

            cancellationToken.ThrowIfCancellationRequested();
            var pipe = ProtectedUpdateProtocol.PipePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat);
            using var child = WindowsUpdateAccess.Launch(executable, pipe, Environment.ProcessId);
            var ticket = new ProtectedUpdateTicket(pipe, child.Id, Guid.NewGuid());
            var operation = installed ? ProtectedUpdateOperation.OpenInstalled : id == Guid.Empty ? ProtectedUpdateOperation.Prepare : ProtectedUpdateOperation.OpenRecovery;
            var message = new ProtectedUpdateMessage(ticket, operation, authorization, root, id, ancestor, identity);
            var reply = await ExchangeAsync(message, cancellationToken).ConfigureAwait(false);
            var prepared = reply.Prepared ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperDidNotReturnAPreparedInstallation);
            if (!prepared.InstallationRoot.Equals(root, StringComparison.OrdinalIgnoreCase) || prepared.HelperContent != helper.Content || prepared.HelperVersion != helper.Version || prepared.Version != manifest.IronmonVersion || prepared.TransactionId == Guid.Empty || (id != Guid.Empty && prepared.TransactionId != id))
                throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorResultDoesNotMatchTheReviewedRelease);

            return prepared with { Elevation = ticket };
        }
        finally
        {
            foreach (var handle in handles)
                handle.Dispose();
        }
    }

    /// <summary>
    /// Uses the signed retained helper for recovery without requiring a network connection.
    /// </summary>
    /// <param name="authorization">The authenticated installation selection.</param>
    /// <param name="manifest">The verified release manifest.</param>
    /// <param name="staging">The private launch staging directory.</param>
    /// <param name="retained">Whether the operation may use its existing recovery copy.</param>
    /// <param name="cancellationToken">The verification token.</param>
    /// <returns>The authenticated staged executable identity.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task<StagedRecoveryHelper> StageHelperAsync(IronmonUpdateAuthorization authorization, ReleaseManifest manifest, string staging, bool retained, CancellationToken cancellationToken)
    {
        if (retained)
        {
            var inventories = new SignedIronmonAuthority(_verifier, new TrackerRuntimeCompatibility()).Resolve(authorization);
            if (RecoveryHelperPackage.ResolveIdentity(manifest, inventories.Target) is { } helper)
            {
                var relative = InstallationLease.StateDirectory + '/' + RecoveryHelperPackage.DirectoryName + '/' + helper.Version + '/' + UpdaterHandoff.HelperFileName;
                var source = PlainPaths.Child(authorization.Request.InstallationRoot, relative);
                await TransactionStorage.CopyAsync(source, PlainPaths.Child(staging, relative), helper.Content, cancellationToken).ConfigureAwait(false);
                return helper;
            }
        }

        return await RecoveryHelperPackage.StageAsync(staging, manifest, _downloads, authorization.Request.Flavor, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Revalidates the bound administrator transaction before normal UI shutdown.
    /// </summary>
    /// <param name="prepared">The selected administrator preparation.</param>
    /// <param name="cancellationToken">The validation token.</param>
    /// <returns>The independent validation.</returns>
    public static async Task ValidateAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
        => _ = await ExecuteAsync(prepared, ProtectedUpdateOperation.Validate, cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Applies the exact preparation while the worker independently requires an idle installation.
    /// </summary>
    /// <param name="prepared">The selected administrator preparation.</param>
    /// <param name="cancellationToken">The safe cancellation token.</param>
    /// <returns>The durable application outcome.</returns>
    public static async Task<TransactionResult> ApplyAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
        => (await ExecuteAsync(prepared, ProtectedUpdateOperation.Apply, cancellationToken: cancellationToken).ConfigureAwait(false)).Result ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperOmittedTheInstallationResult);

    /// <summary>
    /// Restores the selected independently authenticated transaction.
    /// </summary>
    /// <param name="prepared">The selected administrator recovery.</param>
    /// <param name="cancellationToken">The recovery token.</param>
    /// <returns>The durable recovery outcome.</returns>
    public static async Task<TransactionResult> RecoverAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
        => (await ExecuteAsync(prepared, ProtectedUpdateOperation.Recover, cancellationToken: cancellationToken).ConfigureAwait(false)).Result ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperOmittedTheRecoveryResult);

    /// <summary>
    /// Discards only an untouched administrator preparation.
    /// </summary>
    /// <param name="prepared">The selected preparation.</param>
    /// <param name="cancellationToken">The validation token.</param>
    /// <returns>The safe discard.</returns>
    public static async Task DiscardAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
        => _ = await ExecuteAsync(prepared, ProtectedUpdateOperation.Discard, cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Stores bounded navigation without granting the elevated worker permission to launch the tracker.
    /// </summary>
    /// <param name="prepared">The bound preparation.</param>
    /// <param name="navigation">The opaque original tracker context.</param>
    /// <param name="cancellationToken">The handoff token.</param>
    /// <returns>The protected resume record write.</returns>
    public static async Task PreserveAsync(PreparedIronmonUpdate prepared, string navigation, CancellationToken cancellationToken = default)
        => _ = await ExecuteAsync(prepared, ProtectedUpdateOperation.PreserveNavigation, navigation, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Downloads optional sprite sheets through the fixed worker service after core commit.
    /// </summary>
    /// <param name="prepared">The committed protected installation.</param>
    /// <param name="includeUnavailable">Whether previously unavailable sheets are explicitly included.</param>
    /// <param name="cancellationToken">The optional download token.</param>
    /// <returns>The measured optional library outcome.</returns>
    public static async Task<ProtectedSpriteResult> InstallSpritesAsync(PreparedIronmonUpdate prepared, bool includeUnavailable = false, CancellationToken cancellationToken = default)
        => (await ExchangeAsync(new ProtectedUpdateMessage(prepared.Elevation ?? throw new InvalidOperationException(UpdaterText.ProtectedUpdateClientNoAdministratorSessionIsAvailable), ProtectedUpdateOperation.Sprites, null, prepared.InstallationRoot, prepared.TransactionId, IncludeUnavailable: includeUnavailable), cancellationToken).ConfigureAwait(false)).Sprites ?? throw new InvalidDataException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperOmittedTheSpriteResult);

    /// <summary>
    /// Closes the administrator worker after completion without terminating an application process.
    /// </summary>
    /// <param name="prepared">The live administrator session.</param>
    /// <param name="cancellationToken">The disconnect token.</param>
    /// <returns>The acknowledged session closure.</returns>
    public static async Task CloseAsync(PreparedIronmonUpdate prepared, CancellationToken cancellationToken = default)
        => _ = await ExecuteAsync(prepared, ProtectedUpdateOperation.Close, cancellationToken: cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Releases a completed administrator worker without turning a lost cleanup acknowledgement into an installation failure.
    /// </summary>
    /// <param name="prepared">The completed session.</param>
    /// <returns>A bounded best-effort disconnect; abandoned workers also have an idle deadline.</returns>
    public static async Task EndSessionAsync(PreparedIronmonUpdate prepared)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await CloseAsync(prepared, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or OperationCanceledException)
        {
        }
    }

    /// <summary>
    /// Sends only a fixed operation on the already selected transaction.
    /// </summary>
    /// <param name="prepared">The selected preparation.</param>
    /// <param name="operation">The closed operation.</param>
    /// <param name="navigation">Optional bounded navigation.</param>
    /// <param name="cancellationToken">The safe cancellation token.</param>
    /// <returns>The authenticated worker response.</returns>
    private static Task<ProtectedUpdateReply> ExecuteAsync(PreparedIronmonUpdate prepared, ProtectedUpdateOperation operation, string? navigation = null, CancellationToken cancellationToken = default)
        => ExchangeAsync(new ProtectedUpdateMessage(prepared.Elevation ?? throw new InvalidOperationException(UpdaterText.ProtectedUpdateClientNoAdministratorSessionIsAttachedToThisInstallation), operation, null, prepared.InstallationRoot, prepared.TransactionId, Navigation: navigation), cancellationToken);

    /// <summary>
    /// Consumes numeric intermediate frames until safe completion, retaining the original cancellation boundary.
    /// </summary>
    /// <param name="stream">The authenticated administrator response stream.</param>
    /// <param name="cancellationToken">The bounded response token.</param>
    /// <returns>The terminal result after all progress frames.</returns>
    internal static async Task<ProtectedUpdateReply> ReadReplyAsync(Stream stream, CancellationToken cancellationToken)
    {
        while (true)
        {
            var reply = await ProtectedUpdateProtocol.ReadAsync<ProtectedUpdateReply>(stream, cancellationToken).ConfigureAwait(false);
            if (reply.Progress is not { } value)
                return reply;

            if (reply.Prepared is not null || reply.Result is not null || reply.Error is not null || reply.Cancelled || reply.Sprites is not null)
                throw new InvalidDataException(UpdaterText.ProgressInvalidResponse);

            InstallationProgressScope.Report(value);
        }
    }

    /// <summary>
    /// Verifies the actual elevated peer and waits for safe cancellation instead of abandoning live writes.
    /// </summary>
    /// <param name="message">The strictly bounded request.</param>
    /// <param name="cancellationToken">The caller's cancellation request.</param>
    /// <returns>The completed typed reply.</returns>
    internal static async Task<ProtectedUpdateReply> ExchangeAsync(ProtectedUpdateMessage message, CancellationToken cancellationToken)
    {
        ProtectedUpdateProtocol.Validate(message.Ticket);
        using var connect = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connect.CancelAfter(TimeSpan.FromSeconds(45));
        await using var pipe = new NamedPipeClientStream(ProtectedUpdateProtocol.LocalServer, message.Ticket.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(connect.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperDidNotBecomeAvailableRetryThe);
        }

        if (ProtectedUpdateProtocol.ServerId(pipe) != message.Ticket.ProcessId)
            throw new IOException(UpdaterText.ProtectedUpdateClientAnUnexpectedProcessOwnsTheAdministratorEndpoint);

        await ProtectedUpdateProtocol.WriteAsync(pipe, message with { ProgressEnabled = true }, connect.Token).ConfigureAwait(false);
        using var operationTimeout = new CancellationTokenSource(TimeSpan.FromHours(4));
        var response = ReadReplyAsync(pipe, operationTimeout.Token);
        var cancelled = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        if (await Task.WhenAny(response, cancelled).ConfigureAwait(false) == cancelled)
        {
            await pipe.WriteAsync(new byte[] { 1 }, operationTimeout.Token).ConfigureAwait(false);
            await pipe.FlushAsync(operationTimeout.Token).ConfigureAwait(false);
            operationTimeout.CancelAfter(TimeSpan.FromMinutes(3));
        }

        ProtectedUpdateReply reply;
        try
        {
            reply = await response.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operationTimeout.IsCancellationRequested)
        {
            throw new IOException(UpdaterText.ProtectedUpdateClientTheAdministratorHelperHasNotConfirmedCompletionKeepThe);
        }

        if (reply.Cancelled)
            throw new OperationCanceledException(UpdaterText.ProtectedUpdateClientAdministratorWorkWasCancelledSafelyRecoveryFilesWereRetained, cancellationToken);

        if (reply.Error is not null)
            throw new IOException(reply.Error);

        return reply;
    }
}
