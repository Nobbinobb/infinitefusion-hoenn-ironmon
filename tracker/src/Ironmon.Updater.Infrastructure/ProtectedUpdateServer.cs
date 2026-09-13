using Ironmon.Updater.Core;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ironmon.Updater.Infrastructure;

/// <summary>
/// Hosts one administrator capability with authenticated peers and bounded lifetime.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ProtectedUpdateServer
{
    private const string Downloads = "downloads";
    private const string GitHome = "git-home";
    private const string GameStaging = "game-staging";

    /// <summary>
    /// Gets the fixed administrator entry-point argument.
    /// </summary>
    public static string Argument => ProtectedUpdateProtocol.Argument;

    /// <summary>
    /// Runs the isolated worker without launching the tracker or handling arbitrary executable commands.
    /// </summary>
    /// <param name="pipeName">The random endpoint selected by the original UI.</param>
    /// <param name="ownerId">The actual original UI process.</param>
    /// <param name="sprites">The helper's fixed optional sprite service.</param>
    /// <returns>The completed administrator session.</returns>
    public static async Task RunAsync(string pipeName, int ownerId, Func<string, bool, CancellationToken, Task<ProtectedSpriteResult>> sprites)
    {
        if (!WindowsUpdateAccess.IsAdministrator)
            throw new UnauthorizedAccessException(UpdaterText.ProtectedUpdateServerWindowsDidNotGrantAdministratorPermission);

        ProtectedUpdateProtocol.Validate(new ProtectedUpdateTicket(pipeName, Environment.ProcessId, Guid.NewGuid()));
        using var owner = Process.GetProcessById(ownerId);
        var ownerIdentity = UpdateProcessIdentity.Capture(owner);
        using var executable = WindowsUpdateAccess.OpenRegular(Environment.ProcessPath ?? throw new IOException(UpdaterText.ProtectedUpdateServerTheHelperExecutableCouldNotBeLocated));
        var workspace = WindowsUpdateAccess.CreateWorkspace();
        using var http = new ReleaseHttpClient();
        using var source = new GitHubArtifactSource();
        var verifier = UpdaterTrust.CreateVerifier();
        var downloads = new ReleaseDownloadStore(PlainPaths.Child(workspace, Downloads), http);
        var cache = new MinGitCache(WindowsUpdateAccess.GitCachePath(), source);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        var game = new CombinedGamePreparation(cache, policy, PlainPaths.Child(workspace, GameStaging));
        var verification = new CombinedGitVerification(cache, policy, PlainPaths.Child(workspace, GitHome));
        using var worker = new ProtectedUpdateWorker(verifier, downloads, new TrackerRuntimeCompatibility(), game, verification, sprites);
        await using var pipe = CreatePipe(pipeName);
        ProtectedUpdateTicket? ticket = null;
        try
        {
            while (true)
            {
                using var idle = new CancellationTokenSource(ticket is null ? TimeSpan.FromSeconds(45) : TimeSpan.FromMinutes(15));
                await pipe.WaitForConnectionAsync(idle.Token).ConfigureAwait(false);
                var peer = ProtectedUpdateProtocol.ClientId(pipe);
                if (!await IsPeerAsync(peer, ownerIdentity, worker.Prepared, idle.Token).ConfigureAwait(false))
                    throw new IOException(UpdaterText.ProtectedUpdateServerAnUnexpectedProcessConnectedToTheAdministratorSession);

                var message = await ProtectedUpdateProtocol.ReadAsync<ProtectedUpdateMessage>(pipe, idle.Token).ConfigureAwait(false);
                ProtectedUpdateProtocol.Validate(message.Ticket);
                if (message.Ticket.ProcessId != Environment.ProcessId || message.Ticket.PipeName != pipeName || (ticket is not null && ticket != message.Ticket))
                    throw new InvalidDataException(UpdaterText.ProtectedUpdateServerTheAdministratorCapabilityDoesNotMatchThisSession);

                using var cancellation = new CancellationTokenSource();
                using var stopWatching = new CancellationTokenSource();
                var watching = WatchCancellationAsync(pipe, cancellation, stopWatching.Token);
                var measurements = System.Threading.Channels.Channel.CreateBounded<InstallationProgress>(new System.Threading.Channels.BoundedChannelOptions(1) { FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest, SingleReader = true });
                using var progress = new InstallationProgressScope(value => { if (message.ProgressEnabled) measurements.Writer.TryWrite(value); });
                var reporting = ReportAsync(pipe, measurements.Reader, cancellation);
                ProtectedUpdateReply reply;
                var initial = ticket is null;
                ticket ??= message.Ticket;
                try
                {
                    reply = initial
                        ? await worker.InitializeAsync(message, cancellation.Token).ConfigureAwait(false)
                        : await worker.ExecuteAsync(message, cancellation.Token).ConfigureAwait(false);
                    if (cancellation.IsCancellationRequested && reply.Result is null && reply.Sprites is null)
                        reply = reply with { Cancelled = true };
                }
                catch (OperationCanceledException)
                {
                    reply = new ProtectedUpdateReply(Cancelled: true);
                }
                catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or System.Net.Http.HttpRequestException or System.Security.Cryptography.CryptographicException or System.Text.Json.JsonException)
                {
                    reply = new ProtectedUpdateReply(Error: error.Message.Length <= 4096 ? error.Message : error.Message[..4096]);
                }
                finally
                {
                    measurements.Writer.TryComplete();
                    try
                    {
                        await reporting.ConfigureAwait(false);
                    }
                    finally
                    {
                        stopWatching.Cancel();
                        await watching.ConfigureAwait(false);
                    }
                }

                using var responseTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await ProtectedUpdateProtocol.WriteAsync(pipe, reply, responseTimeout.Token).ConfigureAwait(false);
                pipe.Disconnect();
                if (message.Operation == ProtectedUpdateOperation.Close || (initial && (reply.Error is not null || reply.Cancelled)))
                    break;
            }
        }
        finally
        {
            await worker.AbandonAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Streams only the latest measurement without blocking installation work on a slow UI.
    /// </summary>
    /// <param name="pipe">The authenticated peer stream.</param>
    /// <param name="measurements">The bounded latest-value queue.</param>
    /// <param name="cancellation">The operation cancellation boundary.</param>
    /// <returns>The serialized progress writes, completed before the terminal result.</returns>
    private static async Task ReportAsync(Stream pipe, System.Threading.Channels.ChannelReader<InstallationProgress> measurements, CancellationTokenSource cancellation)
    {
        try
        {
            await foreach (var value in measurements.ReadAllAsync().ConfigureAwait(false))
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await ProtectedUpdateProtocol.WriteAsync(pipe, new ProtectedUpdateReply(Progress: value), timeout.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates an endpoint usable across UAC credential identities while rejecting network clients.
    /// </summary>
    /// <param name="name">The unguessable pipe name.</param>
    /// <returns>The exclusively owned pipe instance.</returns>
    internal static NamedPipeServerStream CreatePipe(string name)
    {
        var security = new PipeSecurity();
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance, 4096, 4096, security);
    }

    /// <summary>
    /// Accepts only the original live UI or the authenticated retained helper for this installation.
    /// </summary>
    /// <param name="id">The Windows-reported peer.</param>
    /// <param name="owner">The original UI identity.</param>
    /// <param name="prepared">The fixed handoff target after preparation.</param>
    /// <param name="cancellationToken">The identity-check token.</param>
    /// <returns>Whether this process may present the session capability.</returns>
    private static async Task<bool> IsPeerAsync(uint id, UpdateProcessIdentity owner, PreparedIronmonUpdate? prepared, CancellationToken cancellationToken)
    {
        if (id == owner.Id)
        {
            using var live = owner.Open();
            return live is not null;
        }

        if (prepared is null)
            return false;

        using var process = Process.GetProcessById(checked((int)id));
        var peer = UpdateProcessIdentity.Capture(process);
        var helper = PlainPaths.Child(prepared.InstallationRoot, InstallationLease.StateDirectory + '/' + RecoveryHelperPackage.DirectoryName + '/' + prepared.HelperVersion + '/' + UpdaterHandoff.HelperFileName);
        return peer.ExecutablePath.Equals(helper, StringComparison.OrdinalIgnoreCase) && await TransactionStorage.ContentAsync(helper, cancellationToken).ConfigureAwait(false) == prepared.HelperContent;
    }

    /// <summary>
    /// Turns an explicit cancellation byte or lost caller into safe engine cancellation.
    /// </summary>
    /// <param name="pipe">The connected operation.</param>
    /// <param name="operation">The worker operation cancellation source.</param>
    /// <param name="stop">Stops watching after the operation has completed.</param>
    /// <returns>The completed cancellation observation.</returns>
    private static async Task WatchCancellationAsync(Stream pipe, CancellationTokenSource operation, CancellationToken stop)
    {
        try
        {
            await pipe.ReadAsync(new byte[1], stop).ConfigureAwait(false);
            operation.Cancel();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
            operation.Cancel();
        }
    }
}
