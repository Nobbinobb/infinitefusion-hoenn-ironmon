using System.ComponentModel;
using System.Runtime.InteropServices;
using Ironmon.Updater.Infrastructure;
using Microsoft.Win32.SafeHandles;
using Xunit.Abstractions;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Reproduces Windows directory sharing failures using only handles and temporary trees owned by each fixture.
/// </summary>
/// <remarks>
/// Initializes native contention regressions with retained error-code diagnostics.
/// </remarks>
/// <param name="output">The current test's diagnostic sink.</param>
public sealed class WindowsFileContentionTests(ITestOutputHelper output)
{
    private const string _cache = "cache";
    private const string _runtime = "runtime";
    private const string _stages = "stage-*";
    private const string _source = "source";
    private const string _destination = "destination";
    private const string _damaged = "changed after authentication";
    private const int _nativeErrorMask = 0xffff;
    private const int _accessDenied = 5;
    private const int _sharingViolation = 32;
    private const int _lockViolation = 33;
    private const int _releaseDelayMilliseconds = 150;

    /// <summary>
    /// Identifies the native error produced when another reader prevents renaming the verified runtime directory.
    /// </summary>
    /// <param name="populated">Whether the locked directory contains a file like a verified MinGit stage.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryLeaseReproducesNativePromotionFailure(bool populated)
    {
        using TestWorkspace workspace = new();
        string source = workspace.PathFor(_source);
        string destination = workspace.PathFor(_destination);
        Directory.CreateDirectory(source);
        if (populated)
            File.WriteAllText(Path.Combine(source, MinGitPackage.License), TestArchives.FakeLicense);

        using SafeFileHandle held = DirectoryLease.Open(source);
        Exception error = Assert.ThrowsAny<IOException>(() => Directory.Move(source, destination));
        int code = error.HResult & _nativeErrorMask;
        output.WriteLine($"A non-delete-sharing directory handle (populated: {populated}) causes {error.GetType().Name}, HRESULT 0x{error.HResult:X8}, Win32 {code}.");
        Assert.Contains(code, new[] { _accessDenied, _sharingViolation, _lockViolation });
        Assert.True(Directory.Exists(source));
        Assert.False(Directory.Exists(destination));
    }

    /// <summary>
    /// Completes the real acquisition after a temporary external directory reader releases the authenticated staging tree.
    /// </summary>
    /// <returns>Completion after the private runtime is published and the stage is cleaned.</returns>
    [Fact]
    public async Task CachePromotionWaitsForTemporaryDirectoryLease()
    {
        using TestWorkspace workspace = new();
        using LockedCache fixture = new(workspace, state => state.Release());
        string executable = await fixture.Cache.AcquireAsync();
        Assert.True(fixture.Clock.RetryTimers > 0);
        Assert.Equal(TestArchives.FakeExecutable, await File.ReadAllTextAsync(executable));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Root, _stages));
    }

    /// <summary>
    /// Rejects changed staged bytes after a sharing failure instead of publishing the formerly authenticated tree.
    /// </summary>
    /// <returns>Completion after the changed source is rejected and its stage is removed.</returns>
    [Fact]
    public async Task CachePromotionReauthenticatesAfterContention()
    {
        using TestWorkspace workspace = new();
        using LockedCache fixture = new(workspace, state =>
        {
            File.WriteAllText(Path.Combine(state.StagedRuntime, MinGitPackage.Executable), _damaged);
            state.Release();
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Cache.AcquireAsync());
        Assert.True(fixture.Clock.RetryTimers > 0);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, _runtime)));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Root, _stages));
    }

    /// <summary>
    /// Cancels a blocked acquisition without publishing a runtime or leaving an unlocked partial stage.
    /// </summary>
    /// <returns>Completion after caller cancellation and owned-stage cleanup.</returns>
    [Fact]
    public async Task CachePromotionRemainsCancellableDuringContention()
    {
        using TestWorkspace workspace = new();
        using CancellationTokenSource cancellation = new();
        using LockedCache fixture = new(workspace, state =>
        {
            state.Release();
            cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Cache.AcquireAsync(cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, _runtime)));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Root, _stages));
    }

    /// <summary>
    /// Preserves failure when the native directory owner never releases its lease within the bounded promotion wait.
    /// </summary>
    /// <returns>Completion after the acquisition fails without reporting an executable.</returns>
    [Fact]
    public async Task PersistentCacheLeaseCannotReportSuccessfulAcquisition()
    {
        using TestWorkspace workspace = new();
        using LockedCache fixture = new(workspace);
        Exception? error = await Record.ExceptionAsync(() => fixture.Cache.AcquireAsync());
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.True(fixture.Clock.RetryTimers > 0);
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, _runtime)));
    }

    /// <summary>
    /// Removes only the owned tree after a temporary native directory reader releases it.
    /// </summary>
    /// <returns>Completion after the owned lock and cleanup operation finish.</returns>
    [Fact]
    public async Task OwnedCleanupWaitsForTemporaryDirectoryLease()
    {
        using TestWorkspace workspace = new();
        string source = workspace.PathFor(_source);
        Directory.CreateDirectory(source);
        using SafeFileHandle held = DirectoryLease.Open(source);
        Task release = ReleaseLaterAsync(held);
        PlainPaths.DeleteOwned(workspace.Root, source);
        await release;
        Assert.False(Directory.Exists(source));
    }

    /// <summary>
    /// Preserves an error when an owned directory cannot be removed within the bounded wait.
    /// </summary>
    [Fact]
    public void PersistentCleanupLeaseCannotReportSuccess()
    {
        using TestWorkspace workspace = new();
        string source = workspace.PathFor(_source);
        Directory.CreateDirectory(source);
        using SafeFileHandle held = DirectoryLease.Open(source);
        Exception? error = Record.Exception(() => PlainPaths.DeleteOwned(workspace.Root, source));
        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.True(Directory.Exists(source));
    }

    /// <summary>
    /// Releases only this fixture's handle after an interval longer than the first cleanup attempt.
    /// </summary>
    /// <param name="held">The owned directory handle.</param>
    /// <returns>Completion after the temporary native lease is released.</returns>
    private static async Task ReleaseLaterAsync(IDisposable held)
    {
        await Task.Delay(_releaseDelayMilliseconds).ConfigureAwait(false);
        held.Dispose();
    }

    /// <summary>
    /// Precreates and holds only the actual acquisition's staging directory before extraction begins.
    /// </summary>
    private sealed class LockedCache : IDisposable
    {
        private SafeFileHandle? _held;

        /// <summary>
        /// Creates a production cache whose first stage is held by an independent native directory lease.
        /// </summary>
        /// <param name="workspace">The fixture's disposable workspace.</param>
        /// <param name="onRetry">The optional action performed when acquisition first waits for contention.</param>
        internal LockedCache(TestWorkspace workspace, Action<LockedCache>? onRetry = null)
        {
            Root = workspace.PathFor(_cache);
            Clock = new RetryObserverClock(() => onRetry?.Invoke(this));
            (byte[] bytes, MinGitPackage package) = TestArchives.Create();
            MemoryArtifact source = new(bytes)
            {
                DuringDownload = () =>
                {
                    string stage = Directory.GetDirectories(Root, _stages).Single();
                    StagedRuntime = Path.Combine(stage, _runtime);
                    Directory.CreateDirectory(StagedRuntime);
                    _held = DirectoryLease.Open(StagedRuntime);
                }
            };

            Cache = new MinGitCache(Root, source, package, Clock);
        }

        /// <summary>
        /// Gets the bounded private cache root.
        /// </summary>
        internal string Root { get; }

        /// <summary>
        /// Gets the actual locked extraction directory after download begins.
        /// </summary>
        internal string StagedRuntime { get; private set; } = string.Empty;

        /// <summary>
        /// Gets the clock observing the production contention wait.
        /// </summary>
        internal RetryObserverClock Clock { get; }

        /// <summary>
        /// Gets the production cache exercising authentication and promotion.
        /// </summary>
        internal MinGitCache Cache { get; }

        /// <summary>
        /// Releases the exact staging-directory lease owned by the fixture.
        /// </summary>
        internal void Release()
            => _held?.Dispose();

        /// <summary>
        /// Releases the fixture's native lease before its workspace is removed.
        /// </summary>
        public void Dispose()
            => Release();
    }

    /// <summary>
    /// Observes real acquisition retry timers without replacing file operations or advancing deadlines artificially.
    /// </summary>
    /// <remarks>
    /// Initializes the action triggered when an actual contention wait begins.
    /// </remarks>
    /// <param name="onRetry">The action that releases, cancels, or changes the independently held staging tree.</param>
    private sealed class RetryObserverClock(Action onRetry) : TimeProvider
    {
        private const int _retryMilliseconds = 100;

        /// <summary>
        /// Gets the number of contention waits requested by the real acquisition path.
        /// </summary>
        internal int RetryTimers { get; private set; }

        /// <summary>
        /// Starts a real system timer while observing the acquisition's short contention waits.
        /// </summary>
        /// <param name="callback">The callback to run after the timer elapses.</param>
        /// <param name="state">The opaque callback state.</param>
        /// <param name="dueTime">The interval before the first callback.</param>
        /// <param name="period">The interval for repeated callbacks, or an infinite interval for a one-shot timer.</param>
        /// <returns>The owned system timer.</returns>
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime == TimeSpan.FromMilliseconds(_retryMilliseconds))
            {
                RetryTimers++;
                onRetry();
            }

            return TimeProvider.System.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>
    /// Opens a native lease on one disposable directory without locking any installed-game ancestor.
    /// </summary>
    private static class DirectoryLease
    {
        private const string _kernel = "kernel32.dll";
        private const uint _openExisting = 3;
        private const uint _backupSemantics = 0x02000000;
        private const uint _genericRead = 0x80000000;

        /// <summary>
        /// Holds one fixture directory with ordinary read/write sharing while denying its rename or deletion.
        /// </summary>
        /// <param name="path">The existing disposable directory.</param>
        /// <returns>The exact native directory handle owned by the caller.</returns>
        internal static SafeFileHandle Open(string path)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Native directory sharing regressions require Windows.");

            SafeFileHandle handle = CreateFileW(path, _genericRead, FileShare.ReadWrite, IntPtr.Zero, _openExisting, _backupSemantics, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error);
            }

            return handle;
        }

        /// <summary>
        /// Opens only the named fixture directory using native directory semantics.
        /// </summary>
        /// <param name="name">The absolute directory path.</param>
        /// <param name="access">The requested metadata access mask.</param>
        /// <param name="share">The permitted sharing modes.</param>
        /// <param name="security">The unused security descriptor.</param>
        /// <param name="creation">The open-existing mode.</param>
        /// <param name="flags">The native backup-semantics directory flag.</param>
        /// <param name="template">The unused template handle.</param>
        /// <returns>The owned directory handle.</returns>
        [DllImport(_kernel, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string name, uint access, FileShare share, IntPtr security, uint creation, uint flags, IntPtr template);
    }
}
