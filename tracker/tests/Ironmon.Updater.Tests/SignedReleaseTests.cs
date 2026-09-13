using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;
using System.Diagnostics;
using System.Text;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Exercises the complete signed Ironmon-only preparation and transaction path without game downloads.
/// </summary>
public sealed class SignedReleaseTests
{
    private const string OldHelper = ".ironmon-update/recovery/1/Ironmon.Updater.exe";
    private const string NewHelper = ".ironmon-update/recovery/1.0.1/Ironmon.Updater.exe";
    private const string FutureEngine = "2.0.0";
    private const string InvalidJson = "{\"schemaVersion\":1,\"schemaVersion\":1}";
    private const string NullArray = "[null]";
    private const string BadVersion = "0.09.0";
    private const string UnrelatedRoot = "unrelated";
    private const string GitDirectory = ".git";
    private const string SignedApplyMode = "signed-apply";
    private const string ReleaseProbeMode = "--release-probe";
    private const string GameText = "Data/game.txt";
    private const string UnixText = "fixture\n";
    private const string WindowsText = "fixture\r\n";
    private const string EditedText = "edited\r\n";
    private const string NumericBefore = "0.9.0";
    private const string NumericAfter = "0.10.0";
    private const string FutureHelperName = "Ironmon-Updater-v2.0.0.zip";
    private const string ZipSuffix = ".zip";

    /// <summary>
    /// Reconstructs the authority independently, commits matching components and retains user data and old helpers.
    /// </summary>
    /// <param name="flavor">The authenticated target flavor.</param>
    [Theory]
    [InlineData(ReleaseProtocol.SelfContained)]
    [InlineData(ReleaseProtocol.RuntimeRequired)]
    public async Task SignedUpdateCommitsAllComponentsAndPreservesUserFiles(string flavor)
    {
        using var fixture = new SignedReleaseFixture(flavor);
        fixture.Write(OldHelper, [11]);
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(flavor), fixture.Evidence);
        Assert.Equal(SignedReleaseFixture.VersionA, await File.ReadAllTextAsync(Path.Combine(fixture.Root, SignedReleaseFixture.Script)));
        Assert.Equal(new byte[] { 5 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, NewHelper)));
        var authority = new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime());
        var engine = new UpdateTransaction(authority, _ => Task.CompletedTask);
        var restarted = false;
        var result = await engine.ApplyAsync(fixture.Root, prepared.TransactionId, () =>
        {
            foreach (var path in new[] { UpdaterHandoff.TrackerRelativePath, SignedReleaseFixture.Script, SignedReleaseFixture.Data })
                Assert.Equal(SignedReleaseFixture.VersionB, File.ReadAllText(Path.Combine(fixture.Root, path)));

            restarted = true;
            return Task.CompletedTask;
        });
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Null(result.RelaunchError);
        Assert.True(restarted);
        Assert.False(File.Exists(Path.Combine(fixture.Root, SignedReleaseFixture.Obsolete)));
        Assert.Equal("\t"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(fixture.Root, SignedReleaseFixture.Profile)));
        Assert.Equal("*"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(fixture.Root, SignedReleaseFixture.Save)));
        Assert.Equal("+"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(fixture.Root, SignedReleaseFixture.Settings)));
        Assert.Equal(new byte[] { 11 }, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, OldHelper)));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, GitDirectory)));
        Assert.All(fixture.Requests, request => Assert.StartsWith(ReleaseProtocol.ReleaseBaseUrl, request.AbsoluteUri, StringComparison.Ordinal));
        var receipt = ReleaseJson.Parse<IronmonInstalledReceipt>(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, IronmonOnlyUpdate.ReceiptPath)), ReleaseJson.ManifestLimit);
        Assert.Equal(SignedReleaseFixture.VersionB, receipt.Version);
        Assert.Equal(prepared.Version, receipt.Version);
    }

    /// <summary>
    /// Rejects unavailable or corrupted downloads while preserving every original installed byte.
    /// </summary>
    /// <param name="offline">Whether the transport fails before returning bytes.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedDownloadPreservesVersionA(bool offline)
    {
        using var fixture = new SignedReleaseFixture { Offline = offline, CorruptArchive = !offline };
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Root));
        Assert.False(File.Exists(Path.Combine(fixture.Root, InstallationLease.StateDirectory, InstallationLease.ActiveFile)));
    }

    /// <summary>
    /// Detects missing future runtime prerequisites before replacing any installed files.
    /// </summary>
    [Fact]
    public async Task MissingRuntimePreservesVersionA()
    {
        using var fixture = new SignedReleaseFixture();
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator(true).PrepareAsync(fixture.Request(), fixture.Evidence));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Root));
    }

    /// <summary>
    /// Allows an explicitly selected package-flavor change through the same authenticated transaction.
    /// </summary>
    /// <param name="current">The installed flavor.</param>
    /// <param name="target">The selected replacement flavor.</param>
    [Theory]
    [InlineData(ReleaseProtocol.RuntimeRequired, ReleaseProtocol.SelfContained)]
    [InlineData(ReleaseProtocol.SelfContained, ReleaseProtocol.RuntimeRequired)]
    public async Task ExplicitFlavorSwitchUsesMatchingSignedInventories(string current, string target)
    {
        using var fixture = new SignedReleaseFixture(current);
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(current) with { Flavor = target }, fixture.Evidence);
        var result = await new UpdateTransaction(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask).ApplyAsync(fixture.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        var receipt = ReleaseJson.Parse<IronmonInstalledReceipt>(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, IronmonOnlyUpdate.ReceiptPath)), ReleaseJson.ManifestLimit);
        Assert.Equal(target, receipt.Flavor);
    }

    /// <summary>
    /// Detects unchanged required program files that a generic three-way plan would otherwise preserve incorrectly.
    /// </summary>
    /// <param name="removed">Whether the player removed the required file.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DamagedUnchangedDependencyFailsBeforePreparingTransaction(bool removed)
    {
        using var fixture = new SignedReleaseFixture();
        if (removed)
        {
            File.Delete(Path.Combine(fixture.Root, TrackerRuntimeCompatibility.ConfigurationPath));
        }
        else
        {
            fixture.Write(TrackerRuntimeCompatibility.ConfigurationPath, [99]);
        }

        var before = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence));
        Assert.Contains(TrackerRuntimeCompatibility.ConfigurationPath, error.Message, StringComparison.Ordinal);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Root));
        Assert.False(File.Exists(Path.Combine(fixture.Root, InstallationLease.StateDirectory, InstallationLease.ActiveFile)));
    }

    /// <summary>
    /// Rejects unsupported engines and active-run policies before any network work.
    /// </summary>
    /// <param name="activeRun">Whether the failure is the active-run policy.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task IncompatibleSelectionFailsBeforeDownloads(bool activeRun)
    {
        using var fixture = new SignedReleaseFixture();
        var target = activeRun
            ? fixture.Manifest with { Game = fixture.Manifest.Game with { ActiveRunPolicy = ReleaseProtocol.FinishRun } }
            : fixture.Manifest with { MinimumEngineVersion = FutureEngine, Assets = [.. fixture.Manifest.Assets.Select(asset => asset.Role == ReleaseProtocol.UpdaterRole ? asset with { Name = FutureHelperName, Url = ReleaseProtocol.AssetUrl(SignedReleaseFixture.VersionB, FutureHelperName) } : asset)] };

        var evidence = fixture.Sign(target, fixture.Evidence.Inventories);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Coordinator().PrepareAsync(fixture.Request() with { HasActiveRun = activeRun }, evidence));
        Assert.Empty(fixture.Requests);
    }

    /// <summary>
    /// Prevents an Ironmon-only update from silently repairing an unsupported or modified game.
    /// </summary>
    [Fact]
    public async Task ModifiedGameBlocksWithoutFetchingGameContent()
    {
        using var fixture = new SignedReleaseFixture();
        fixture.Write(SignedReleaseFixture.Game, [99]);
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Root));
        Assert.DoesNotContain(fixture.Requests, request => request.AbsoluteUri.EndsWith(ZipSuffix, StringComparison.Ordinal));
    }

    /// <summary>
    /// Requires explicit exact-path decisions for locally modified managed program files.
    /// </summary>
    [Fact]
    public async Task ModifiedScriptsRequireApprovalAndThenUpdate()
    {
        using var fixture = new SignedReleaseFixture();
        fixture.Write(SignedReleaseFixture.Script, [99]);
        await Assert.ThrowsAsync<IronmonUpdateConflictException>(() => fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence));
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request() with { ApprovedPaths = [SignedReleaseFixture.Script] }, fixture.Evidence);
        var result = await new UpdateTransaction(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask).ApplyAsync(fixture.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
    }

    /// <summary>
    /// Rejects unsigned plan expansion even when the journal and evidence are present locally.
    /// </summary>
    [Fact]
    public async Task HelperReauthenticatesEvidenceAndRejectsChangedPlan()
    {
        using var fixture = new SignedReleaseFixture();
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence);
        var path = Path.Combine(fixture.Root, InstallationLease.StateDirectory, TransactionStorage.TransactionsDirectory, prepared.TransactionId.ToString(TransactionStorage.GuidFormat), TransactionStorage.DescriptionFile);
        var description = TransactionStorage.Deserialize<TransactionDescription>(await File.ReadAllBytesAsync(path));
        var altered = description with { Plan = description.Plan with { Target = [.. description.Plan.Target, new ManagedFile(UnrelatedRoot, new GameFileContent(1, TransactionStorage.Hash([1])), ManagedFileOwner.Ironmon)] } };
        var authority = new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime());
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.AuthorizeAsync(altered, TransactionStorage.Serialize(altered), CancellationToken.None));
        var evidencePath = IronmonOnlyUpdate.AuthorizationPath(fixture.Root, prepared.TransactionId);
        var evidence = ReleaseJson.Parse<IronmonUpdateAuthorization>(await File.ReadAllBytesAsync(evidencePath), 64 * ReleaseJson.ManifestLimit);
        evidence.Target.Manifest[0] ^= 1;
        await File.WriteAllBytesAsync(evidencePath, ReleaseJson.Serialize(evidence));
        await Assert.ThrowsAsync<InvalidDataException>(() => authority.AuthorizeAsync(description, TransactionStorage.Serialize(description), CancellationToken.None));
    }

    /// <summary>
    /// Restores the complete original snapshot when final component verification fails.
    /// </summary>
    [Fact]
    public async Task FinalRuntimeFailureRollsBackAllComponents()
    {
        using var fixture = new SignedReleaseFixture();
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence);
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Verifier, SignedReleaseFixture.Runtime(true)), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(fixture.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.RolledBack, result.Phase);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Root));
    }

    /// <summary>
    /// Accepts only explicitly signed Windows text bytes and never normalizes away local game edits.
    /// </summary>
    [Fact]
    public async Task GameTextRequiresAnExplicitMatchingAlternateHash()
    {
        using var fixture = new SignedReleaseFixture();
        var canonical = Encoding.UTF8.GetBytes(UnixText);
        var windows = Encoding.UTF8.GetBytes(WindowsText);
        var file = SignedReleaseFixture.Entry(GameText, canonical, ReleaseProtocol.GameScope) with { WindowsText = new ReleaseFileFingerprint(windows.LongLength, TransactionStorage.Hash(windows)) };
        var inventory = new ReleaseFileInventory(ReleaseProtocol.InventoryDocument, 1, ReleaseProtocol.GameScope, SignedReleaseFixture.VersionA, SignedReleaseFixture.Commit, null, [file]);
        fixture.Write(GameText, windows);
        await SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None);
        fixture.Write(GameText, canonical);
        await SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None);
        fixture.Write(GameText, Encoding.UTF8.GetBytes(EditedText));
        await Assert.ThrowsAsync<InvalidDataException>(() => SignedIronmonAuthority.VerifyGameAsync(fixture.Root, inventory, CancellationToken.None));
    }

    /// <summary>
    /// Refuses to overwrite a previously staged helper version with different executable bytes.
    /// </summary>
    [Fact]
    public async Task ExistingHelperVersionIsImmutable()
    {
        using var fixture = new SignedReleaseFixture();
        fixture.Write(NewHelper, [99]);
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence));
        Assert.Equal("c"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(fixture.Root, NewHelper)));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Root));
    }

    /// <summary>
    /// Compares stable numeric components instead of sorting their display strings.
    /// </summary>
    [Fact]
    public void StableVersionsUseNumericOrdering()
        => Assert.True(ReleaseProtocol.ParseVersion(NumericAfter) > ReleaseProtocol.ParseVersion(NumericBefore));

    /// <summary>
    /// Applies signed evidence in a fresh process, then launches the installed executable and reads matching script/data versions.
    /// </summary>
    [Fact]
    public async Task IndependentSignedHelperAndInstalledTrackerRunSuccessfully()
    {
        using var fixture = new SignedReleaseFixture(runnableTracker: true);
        fixture.Write(TransactionFixture.FixtureMarker, [1]);
        var prepared = await fixture.Coordinator().PrepareAsync(fixture.Request(), fixture.Evidence);
        var host = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, SignedReleaseFixture.HostRelativePath + SignedReleaseFixture.BuildConfiguration + SignedReleaseFixture.HostSuffix));
        var applied = await RunOwnedProcessAsync(host, [SignedApplyMode, fixture.Root, prepared.TransactionId.ToString(), fixture.PublicKey]);
        Assert.Contains(nameof(TransactionPhase.Committed), applied, StringComparison.Ordinal);
        var started = await RunOwnedProcessAsync(Path.Combine(fixture.Root, UpdaterHandoff.TrackerRelativePath), [ReleaseProbeMode]);
        Assert.Equal(new[] { SignedReleaseFixture.VersionB, SignedReleaseFixture.VersionB }, started.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Starts and bounds only a test-owned process, preserving its complete diagnostic output.
    /// </summary>
    /// <param name="executable">The fixture executable.</param>
    /// <param name="arguments">The fixture-only arguments.</param>
    /// <returns>The successful process output.</returns>
    private static async Task<string> RunOwnedProcessAsync(string executable, string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new IOException("The signed fixture process could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.True(process.ExitCode == 0, await output + await error);
            return await output;
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    /// <summary>
    /// Binds authentication to exact bytes and independently configured signing keys.
    /// </summary>
    [Fact]
    public void ExactBytesAndEmbeddedKeysAreRequired()
    {
        using var fixture = new SignedReleaseFixture();
        var evidence = fixture.Evidence;
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Verifier.Verify(evidence.Manifest, evidence.Signatures).IronmonVersion);
        Assert.Throws<InvalidDataException>(() => fixture.Verifier.Verify([.. evidence.Manifest, (byte)' '], evidence.Signatures));
        Assert.Throws<InvalidDataException>(() => UpdaterTrust.CreateVerifier().Verify(evidence.Manifest, evidence.Signatures));
        using var unrelated = new SignedReleaseFixture();
        Assert.Throws<InvalidDataException>(() => unrelated.Verifier.Verify(evidence.Manifest, evidence.Signatures));
        var inventory = evidence.Inventories[0] with { Bytes = [.. evidence.Inventories[0].Bytes, (byte)' '] };
        Assert.Throws<InvalidDataException>(() => fixture.Verifier.VerifyInventory(fixture.Manifest, inventory));
    }

    /// <summary>
    /// Rejects parser ambiguity and malformed version identifiers even when a fixture signs them.
    /// </summary>
    [Fact]
    public void SignedMalformedDocumentsRemainInvalid()
    {
        using var fixture = new SignedReleaseFixture();
        foreach (var bytes in new[] { Encoding.UTF8.GetBytes(InvalidJson), new byte[] { 0xEF, 0xBB, 0xBF }.Concat(fixture.Evidence.Manifest).ToArray() })
            Assert.Throws<InvalidDataException>(() => fixture.Verifier.Verify(bytes, fixture.Signature(bytes)));

        Assert.Throws<InvalidDataException>(() => ReleaseJson.Validate(Encoding.UTF8.GetBytes(NullArray), 100));
        var invalid = fixture.Sign(fixture.Manifest with { IronmonVersion = BadVersion }, fixture.Evidence.Inventories);
        Assert.Throws<InvalidDataException>(() => fixture.Verifier.Verify(invalid.Manifest, invalid.Signatures));
    }
}
