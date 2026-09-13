using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.CrashHost;

/// <summary>
/// Hosts only disposable test transactions so a parent can terminate the real engine between durable boundaries.
/// </summary>
internal static class Program
{
    private const string ApplyMode = "apply";
    private const string RecoveryMode = "recover";
    private const string FixtureMarker = ".transaction-fixture";
    private const string BoundaryReady = "BOUNDARY";
    private const string WaitMode = "wait";
    private const string HandoffParentMode = "handoff-parent";
    private const string HandoffLog = ".ironmon-update/handoff-fixture.log";
    private const string RejectMarker = ".ironmon-update/reject-fixture";
    private const string HelperPath = ".ironmon-update/recovery/1/Ironmon.Updater.exe";
    private const string Navigation = "archive/fixture";
    private const string Validated = "validated";
    private const string Quiesced = "quiesced";
    private const string Closing = "closing";
    private const string Continued = "continued";
    private const string FailedHandoff = "FAILED";
    private const string SignedApplyMode = "signed-apply";
    private const string SignedGameApplyMode = "signed-game-apply";
    private const string SignedGameRecoverMode = "signed-game-recover";
    private const string ReleaseProbeMode = "--release-probe";
    private const string FixtureKey = "fixture-key";
    private const string ScriptVersion = "Data/Scripts/997_Ironmon/main.rb";
    private const string DataVersion = "Data/Ironmon/catalog.json";

    /// <summary>
    /// Runs a fixture transaction and waits at a selected boundary until the owning test terminates this process.
    /// </summary>
    /// <param name="args">The fixture-only mode, root, transaction, independently held digest, phase, operation and boundary.</param>
    /// <returns>Zero for a verified terminal outcome; nonzero for a fixture failure.</returns>
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 7 && args[0] is SignedGameApplyMode or SignedGameRecoverMode && File.Exists(Path.Combine(args[1], FixtureMarker)))
        {
            if (!OperatingSystem.IsWindows())
                return 2;

            using var source = new GitHubArtifactSource();
            var cache = new MinGitCache(args[4], source);
            var git = new CombinedGitVerification(cache, new RepositoryPolicy(args[5], ReleaseProtocol.GameBranch, true), args[6]);
            var verifier = new ReleaseVerifier(new Dictionary<string, byte[]> { [FixtureKey] = Convert.FromBase64String(args[3]) });
            var authority = new SignedIronmonAuthority(verifier, new TrackerRuntimeCompatibility(), git);
            var combinedEngine = new UpdateTransaction(authority, _ => Task.CompletedTask, progress =>
            {
                if (args[0] == SignedGameApplyMode && progress.Phase == TransactionPhase.Applying && progress.Boundary == TransactionBoundary.GitDetached)
                {
                    Console.WriteLine(BoundaryReady);
                    Console.Out.Flush();
                    Thread.Sleep(Timeout.Infinite);
                }
            });
            var combinedResult = args[0] == SignedGameApplyMode ? await combinedEngine.ApplyAsync(args[1], Guid.Parse(args[2])) : await combinedEngine.RecoverAsync(args[1], Guid.Parse(args[2]));
            Console.WriteLine(combinedResult.Phase);
            return combinedResult.Phase == TransactionPhase.RolledBack ? 0 : 1;
        }

        if (args.Length == 1 && args[0] == ReleaseProbeMode)
        {
            var root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
            Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(root, ScriptVersion)));
            Console.WriteLine(await File.ReadAllTextAsync(Path.Combine(root, DataVersion)));
            return 0;
        }

        if (OperatingSystem.IsWindows() && args.Length == 4 && args[0] == SignedApplyMode && File.Exists(Path.Combine(args[1], FixtureMarker)))
        {
            var verifier = new ReleaseVerifier(new Dictionary<string, byte[]> { [FixtureKey] = Convert.FromBase64String(args[3]) });
            var authority = new SignedIronmonAuthority(verifier, new TrackerRuntimeCompatibility());
            var signedEngine = new UpdateTransaction(authority, _ => Task.CompletedTask);
            var signedResult = await signedEngine.ApplyAsync(args[1], Guid.Parse(args[2]));
            Console.WriteLine(signedResult.Phase);
            Console.WriteLine(signedResult.Error);
            return signedResult.Phase == TransactionPhase.Committed ? 0 : 1;
        }

        if (args.Length == 2 && args[0] == UpdaterHandoff.HandoffArgument)
            return await RunHandoffHelperAsync(args[1]);

        if (args.Length == 2 && args[0] == HandoffParentMode)
            return await RunHandoffParentAsync(args[1]);

        if (args.Length == 1 && args[0] == WaitMode)
        {
            Console.WriteLine(BoundaryReady);
            Console.Out.Flush();
            await Task.Delay(Timeout.Infinite);
            return 0;
        }

        if (args.Length is not (7 or 8) || !(File.Exists(Path.Combine(args[1], FixtureMarker)) || File.Exists(Path.Combine(args[1], InstallationLease.StateDirectory, FixtureMarker))))
            return 2;

        var phase = Enum.Parse<TransactionPhase>(args[4]);
        var operation = int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture);
        var boundary = Enum.Parse<TransactionBoundary>(args[6]);
        var occurrence = args.Length == 8 ? int.Parse(args[7], System.Globalization.CultureInfo.InvariantCulture) : 1;
        var observed = 0;
        var engine = new UpdateTransaction(new FixtureAuthority(args[3], phase == TransactionPhase.RollingBack), _ => Task.CompletedTask, progress =>
        {
            if (progress.Phase == phase && progress.Operation == operation && progress.Boundary == boundary && ++observed == occurrence)
            {
                Console.WriteLine(BoundaryReady);
                Console.Out.Flush();
                Console.ReadLine();
                throw new IOException("The fixture parent released a crash boundary without terminating its owned child.");
            }
        });
        var result = args[0] == ApplyMode
            ? await engine.ApplyAsync(args[1], Guid.Parse(args[2]))
            : args[0] == RecoveryMode ? await engine.RecoverAsync(args[1], Guid.Parse(args[2])) : throw new ArgumentException("Unsupported crash fixture mode.");
        Console.WriteLine(result.Phase);
        return result.Phase is TransactionPhase.Committed or TransactionPhase.RolledBack ? 0 : 1;
    }

    /// <summary>
    /// Acts as a disposable tracker at the exact fixed tracker path to exercise the real pipe protocol.
    /// </summary>
    /// <param name="root">The marker-protected fixture installation.</param>
    /// <returns>The parent fixture exit status.</returns>
    private static async Task<int> RunHandoffParentAsync(string root)
    {
        if (!File.Exists(Path.Combine(root, FixtureMarker)))
            return 2;

        using var current = Process.GetCurrentProcess();
        var helperBytes = await File.ReadAllBytesAsync(Path.Combine(root, HelperPath));
        var expected = new GameFileContent(helperBytes.LongLength, Convert.ToHexString(SHA256.HashData(helperBytes)));
        var request = new UpdaterHandoffRequest(root, Guid.NewGuid(), UpdateProcessIdentity.Capture(current), null, Navigation);
        try
        {
            var helper = await UpdaterHandoff.LaunchAsync(request, expected, token => File.AppendAllLinesAsync(Path.Combine(root, HandoffLog), [Quiesced], token), () => Task.CompletedTask, () => File.AppendAllLinesAsync(Path.Combine(root, HandoffLog), [Closing]));
            Console.WriteLine(JsonSerializer.Serialize(helper));
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine(FailedHandoff);
            Console.WriteLine(error);
            Console.Out.Flush();
            await Task.Delay(Timeout.Infinite);
            return 1;
        }
    }

    /// <summary>
    /// Acts as an independent disposable helper, acknowledging only after fixture validation and continuing only after parent exit.
    /// </summary>
    /// <param name="pipeName">The real bounded handoff pipe.</param>
    /// <returns>The helper fixture exit status.</returns>
    private static async Task<int> RunHandoffHelperAsync(string pipeName)
    {
        try
        {
            var request = await UpdaterHandoff.AcceptAsync(pipeName, async (request, token) =>
            {
                if (!File.Exists(Path.Combine(request.InstallationRoot, FixtureMarker)) || File.Exists(Path.Combine(request.InstallationRoot, RejectMarker)))
                    throw new IOException("The fixture helper rejected readiness.");

                await File.AppendAllLinesAsync(Path.Combine(request.InstallationRoot, HandoffLog), [Validated], token);
            });
            await request.Tracker.WaitForExitAsync(requestClose: false);
            await File.AppendAllLinesAsync(Path.Combine(request.InstallationRoot, HandoffLog), [Continued]);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    /// <summary>
    /// Holds fixture authorization supplied by the owning test process, outside the journal under test.
    /// </summary>
    /// <remarks>
    /// Constructs a test-only verifier. This executable is never included in a production helper or release package.
    /// </remarks>
    /// <param name="hash">The independently supplied fixture descriptor digest.</param>
    /// <param name="rejectFinal">Whether to initiate rollback for recovery-boundary tests.</param>
    private sealed class FixtureAuthority(string hash, bool rejectFinal) : ITransactionAuthority
    {
        /// <summary>
        /// Rejects any descriptor bytes changed after the parent authorized its disposable fixture.
        /// </summary>
        /// <param name="description">The proposed fixture transaction.</param>
        /// <param name="exactBytes">The exact descriptor bytes.</param>
        /// <param name="cancellationToken">The verification token.</param>
        /// <returns>A completed verification or an authorization failure.</returns>
        public Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(hash), SHA256.HashData(exactBytes.Span)))
                throw new InvalidDataException("The fixture descriptor changed after parent authorization.");

            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts deterministic rollback when the test is inspecting rollback boundaries.
        /// </summary>
        /// <param name="description">The fixture transaction.</param>
        /// <param name="cancellationToken">The verification token.</param>
        /// <returns>The selected fixture result.</returns>
        public Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken)
            => rejectFinal ? Task.FromException(new IOException("Fixture final verification failure.")) : Task.CompletedTask;
    }
}
