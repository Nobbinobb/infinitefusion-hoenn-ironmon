using Ironmon.Updater.Infrastructure;
using System.Buffers.Binary;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Checks administrator authority, process framing, cancellation and the ordinary writable-folder path.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtectedUpdateTests
{
    private const string RootName = "installation";
    private const string ChangedName = "changed";
    private const string HookVariable = "DOTNET_STARTUP_HOOKS";
    private const string HookValue = "fixture-hook";
    private const string BundleVariable = "DOTNET_BUNDLE_EXTRACT_BASE_DIR";
    private const string Navigation = "archive/run-123";
    private const string ElevationVerb = "runas";
    private const string ReadFile = "source.bin";

    /// <summary>
    /// Prevents replacing source ancestors during a read and releases them when asynchronous disposal completes.
    /// </summary>
    [Fact]
    public async Task SourceReadKeepsAncestorsStableUntilDisposed()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, ReadFile);
        File.WriteAllBytes(path, [1]);
        var renamed = workspace.PathFor(ChangedName);
        await using (var input = WindowsUpdateAccess.OpenRegular(path))
        {
            Assert.Throws<IOException>(() => Directory.Move(root, renamed));
            Assert.Equal(1, input.ReadByte());
        }

        Directory.Move(root, renamed);
        Assert.True(File.Exists(Path.Combine(renamed, ReadFile)));
    }

    /// <summary>
    /// Reauthenticates real signed fixture components, applies one transaction and confines optional work to the committed game.
    /// </summary>
    [Fact]
    public async Task SignedPreparationAppliesAndAllowsOptionalWorkOnlyAfterCommit()
    {
        using var fixture = new ProtectedUpdateFixture();
        using var worker = fixture.Worker();
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        var prepared = (await worker.InitializeAsync(fixture.Initial(), CancellationToken.None)).Prepared!;
        Assert.Equal(1, fixture.ProtectedWrites);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Sprites), CancellationToken.None));
        await worker.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Validate), CancellationToken.None);
        await worker.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.PreserveNavigation) with { Navigation = Navigation }, CancellationToken.None);
        Assert.Equal(Navigation, TrackerRelaunch.ReadNavigation(prepared.InstallationRoot, prepared.TransactionId));
        var result = await worker.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Apply), CancellationToken.None);
        Assert.Equal(TransactionPhase.Committed, result.Result!.Phase);
        var sprites = await worker.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Sprites), CancellationToken.None);
        Assert.Equal(2, sprites.Sprites!.Downloaded);
        Assert.Equal(1, fixture.SpriteCalls);
        Assert.Equal(new byte[] { 42 }, File.ReadAllBytes(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Save)));
    }

    /// <summary>
    /// Refuses an installation directory swapped while the UAC prompt was open before reaching protected writes.
    /// </summary>
    [Fact]
    public async Task ChangedDirectoryIdentityIsRejectedBeforeWrites()
    {
        using var fixture = new ProtectedUpdateFixture();
        using var worker = fixture.Worker();
        var message = fixture.Initial();
        Directory.Move(fixture.Release.Root, Path.Combine(Path.GetDirectoryName(fixture.Release.Root)!, ChangedName));
        Directory.CreateDirectory(fixture.Release.Root);
        await Assert.ThrowsAsync<IOException>(() => worker.InitializeAsync(message, CancellationToken.None));
        Assert.Equal(0, fixture.ProtectedWrites);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
    }

    /// <summary>
    /// Rejects changed signed authority before preparing any administrator state.
    /// </summary>
    [Fact]
    public async Task InvalidReleaseIsRejectedBeforeWrites()
    {
        using var fixture = new ProtectedUpdateFixture();
        using var worker = fixture.Worker();
        var message = fixture.Initial();
        var evidence = message.Authorization!.Target with { Manifest = [.. message.Authorization.Target.Manifest, 0] };
        await Assert.ThrowsAsync<InvalidDataException>(() => worker.InitializeAsync(message with { Authorization = message.Authorization with { Target = evidence } }, CancellationToken.None));
        Assert.Equal(0, fixture.ProtectedWrites);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
    }

    /// <summary>
    /// Preserves durable recovery after a worker exits between preparation and replacement.
    /// </summary>
    [Fact]
    public async Task NewWorkerRecoversTheSameAuthenticatedTransaction()
    {
        using var fixture = new ProtectedUpdateFixture();
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        PreparedIronmonUpdate prepared;
        using (var first = fixture.Worker())
            prepared = (await first.InitializeAsync(fixture.Initial(), CancellationToken.None)).Prepared!;

        using var recovery = fixture.Worker();
        var message = fixture.Initial() with { Operation = ProtectedUpdateOperation.OpenRecovery, TransactionId = prepared.TransactionId };
        fixture.Release.Offline = true;
        await recovery.InitializeAsync(message, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Apply), CancellationToken.None));
        var result = await recovery.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Recover), CancellationToken.None);
        Assert.Equal(TransactionPhase.RolledBack, result.Result!.Phase);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
    }

    /// <summary>
    /// Cancels an untouched preparation without replacing files or leaving a blocking active marker.
    /// </summary>
    [Fact]
    public async Task AbandonedPreparationIsDiscardedSafely()
    {
        using var fixture = new ProtectedUpdateFixture();
        using var worker = fixture.Worker();
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        await worker.InitializeAsync(fixture.Initial(), CancellationToken.None);
        await worker.AbandonAsync();
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
    }

    /// <summary>
    /// Refuses replacement root, transaction, authorization and unexpected operation fields after preparation.
    /// </summary>
    [Fact]
    public async Task LaterMessagesCannotChangeTheReviewedSelection()
    {
        using var fixture = new ProtectedUpdateFixture();
        using var worker = fixture.Worker();
        var initial = fixture.Initial();
        var prepared = (await worker.InitializeAsync(initial, CancellationToken.None)).Prepared!;
        var message = fixture.Message(prepared, ProtectedUpdateOperation.Apply);
        ProtectedUpdateMessage[] invalid = [message with { Root = Path.Combine(message.Root, ChangedName) }, message with { TransactionId = Guid.NewGuid() }, message with { Authorization = initial.Authorization }, message with { Navigation = Navigation }, message with { Ancestor = message.Root }, message with { Operation = (ProtectedUpdateOperation)99 }];
        foreach (var item in invalid)
            await Assert.ThrowsAsync<InvalidDataException>(() => worker.ExecuteAsync(item, CancellationToken.None));

        await worker.AbandonAsync();
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
    }

    /// <summary>
    /// Detects a denied directory write while leaving ordinary writable destinations unelevated.
    /// </summary>
    [Fact]
    public void PermissionProbeDoesNotChangeExistingFiles()
    {
        using var workspace = new TestWorkspace();
        var root = workspace.PathFor(RootName);
        Directory.CreateDirectory(root);
        var directory = new DirectoryInfo(root);
        var original = directory.GetAccessControl();
        Assert.False(ProtectedUpdateClient.RequiresElevation(root));
        using var user = WindowsIdentity.GetCurrent();
        try
        {
            var denied = directory.GetAccessControl();
            denied.AddAccessRule(new FileSystemAccessRule(user.User!, FileSystemRights.CreateFiles, AccessControlType.Deny));
            directory.SetAccessControl(denied);
            Assert.True(ProtectedUpdateClient.RequiresElevation(root));
        }
        finally
        {
            directory.SetAccessControl(original);
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(root));
    }

    /// <summary>
    /// Requests runas for the fixed helper, sanitizes runtime hooks and restores the UI environment when UAC is declined.
    /// </summary>
    [Fact]
    public void DeclinedUacIsCancellationAndRestoresRuntimeEnvironment()
    {
        var original = Environment.GetEnvironmentVariable(HookVariable);
        try
        {
            Environment.SetEnvironmentVariable(HookVariable, HookValue);
            Assert.Throws<OperationCanceledException>(() => WindowsUpdateAccess.Launch(Environment.ProcessPath!, ProtectedUpdateProtocol.PipePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat), Environment.ProcessId, start =>
            {
                Assert.True(start.UseShellExecute);
                Assert.Equal(ElevationVerb, start.Verb);
                Assert.Equal(ProtectedUpdateProtocol.Argument, start.ArgumentList[0]);
                Assert.Null(Environment.GetEnvironmentVariable(HookVariable));
                Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetEnvironmentVariable(BundleVariable));
                throw new Win32Exception(1223);
            }));
            Assert.Equal(HookValue, Environment.GetEnvironmentVariable(HookVariable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(HookVariable, original);
        }
    }

    /// <summary>
    /// Ensures administrator recovery state has no ordinary-user write grants or inherited permissions.
    /// </summary>
    [Fact]
    public void ProtectedAclKeepsUsersReadOnly()
    {
        var policy = WindowsUpdateAccess.DirectoryPolicy();
        Assert.True(policy.AreAccessRulesProtected);
        var users = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
        var rules = policy.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        var userRule = Assert.Single(rules, rule => rule.IdentityReference == users);
        Assert.Equal(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, userRule.FileSystemRights);
        Assert.Equal(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), policy.GetOwner(typeof(SecurityIdentifier)));
    }

    /// <summary>
    /// Rejects oversized administrator frames before allocating the declared body.
    /// </summary>
    [Fact]
    public async Task OversizedMessageIsRejectedBeforeReadingBody()
    {
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, int.MaxValue);
        using var input = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProtectedUpdateProtocol.ReadAsync<ProtectedUpdateMessage>(input, CancellationToken.None));
    }

    /// <summary>
    /// Rejects a real connected named pipe whose OS-reported PID differs from the elevated process ticket.
    /// </summary>
    [Fact]
    public async Task UnexpectedAdministratorProcessIsRejected()
    {
        var ticket = new ProtectedUpdateTicket(ProtectedUpdateProtocol.PipePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat), Environment.ProcessId + 1, Guid.NewGuid());
        await using var server = new NamedPipeServerStream(ticket.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        var request = new ProtectedUpdateMessage(ticket, ProtectedUpdateOperation.Close, null, Path.GetTempPath(), Guid.NewGuid());
        await Assert.ThrowsAsync<IOException>(() => ProtectedUpdateClient.ExchangeAsync(request, timeout.Token));
        await connected;
    }

    /// <summary>
    /// Verifies the installed signed payload before allowing a later sprite-only session, including offline helper verification.
    /// </summary>
    [Fact]
    public async Task InstalledSessionCannotApplyOrRecoverProgramFiles()
    {
        using var fixture = new ProtectedUpdateFixture();
        using (var installation = fixture.Worker())
        {
            var prepared = (await installation.InitializeAsync(fixture.Initial(), CancellationToken.None)).Prepared!;
            await installation.ExecuteAsync(fixture.Message(prepared, ProtectedUpdateOperation.Apply), CancellationToken.None);
        }

        fixture.Release.Offline = true;
        using var optional = fixture.Worker();
        var opened = (await optional.InitializeAsync(fixture.Initial() with { Operation = ProtectedUpdateOperation.OpenInstalled }, CancellationToken.None)).Prepared!;
        await Assert.ThrowsAsync<InvalidOperationException>(() => optional.ExecuteAsync(fixture.Message(opened, ProtectedUpdateOperation.Apply), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => optional.ExecuteAsync(fixture.Message(opened, ProtectedUpdateOperation.Recover), CancellationToken.None));
        Assert.NotNull((await optional.ExecuteAsync(fixture.Message(opened, ProtectedUpdateOperation.Sprites), CancellationToken.None)).Sprites);
    }

    /// <summary>
    /// Sends cancellation across a real pipe and waits for the worker's safe-cancellation acknowledgement.
    /// </summary>
    [Fact]
    public async Task CancellationWaitsForTheAdministratorReply()
    {
        var ticket = new ProtectedUpdateTicket(ProtectedUpdateProtocol.PipePrefix + Guid.NewGuid().ToString(TransactionStorage.GuidFormat), Environment.ProcessId, Guid.NewGuid());
        await using var server = ProtectedUpdateServer.CreatePipe(ticket.PipeName);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        var handling = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(timeout.Token);
            await ProtectedUpdateProtocol.ReadAsync<ProtectedUpdateMessage>(server, timeout.Token);
            cancellation.Cancel();
            var signal = new byte[1];
            await server.ReadExactlyAsync(signal, timeout.Token);
            Assert.Equal(1, signal[0]);
            await ProtectedUpdateProtocol.WriteAsync(server, new ProtectedUpdateReply(Cancelled: true), timeout.Token);
        });
        var request = new ProtectedUpdateMessage(ticket, ProtectedUpdateOperation.Discard, null, Path.GetTempPath(), Guid.NewGuid());
        await Assert.ThrowsAsync<OperationCanceledException>(() => ProtectedUpdateClient.ExchangeAsync(request, cancellation.Token));
        await handling;
    }
}
