using Ironmon.Updater.Infrastructure;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Verifies real signed tracker workflows through review, interruption, handoff and recovery boundaries.
/// </summary>
public sealed class TrackerUpdateSessionTests
{
    private const string ZipExtension = ".zip";
    private const string LocalEdit = "local edit";
    private const string LaterEdit = "edited again during download";

    /// <summary>
    /// Keeps startup on the tracker and obtains exact review data before downloading program archives.
    /// </summary>
    [Fact]
    public async Task StartupAndReviewDoNotDownloadProgramsOrOpenThePanelAutomatically()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await fixture.Session.StartAsync();
        Assert.False(fixture.Session.IsOpen);
        Assert.True(fixture.Session.ShowStartupPrompt);
        Assert.Equal(SignedReleaseFixture.VersionB, fixture.Session.AvailableVersion);
        Assert.Empty(fixture.Release.Requests);
        await fixture.Session.OpenAsync();
        Assert.True(fixture.Session.IsOpen);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        Assert.NotNull(fixture.Session.Review);
        Assert.DoesNotContain(fixture.Release.Requests, uri => uri.AbsolutePath.EndsWith(ZipExtension, StringComparison.Ordinal));
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        fixture.Session.Close();
        Assert.False(fixture.Session.IsOpen);
    }

    /// <summary>
    /// Dismisses the invitation for one session while a new tracker launch offers the uninstalled release again.
    /// </summary>
    [Fact]
    public async Task LaterLastsOnlyUntilTheNextLaunch()
    {
        using var first = new TrackerUpdateTestFixture();
        await first.Session.StartAsync();
        first.Session.DismissStartupPrompt();
        await first.Session.StartAsync();
        Assert.False(first.Session.ShowStartupPrompt);
        Assert.False(first.Session.IsOpen);
        var requests = first.MetadataRequests;
        await first.Session.OpenFromSettingsAsync();
        Assert.True(first.MetadataRequests > requests);
        Assert.True(first.Session.OpenedFromSettings);
        Assert.True(first.Session.CanUpdate, first.Session.Error);
        first.Session.Close();
        Assert.False(first.Session.ShowStartupPrompt);
        using var next = new TrackerUpdateTestFixture();
        await next.Session.StartAsync();
        Assert.True(next.Session.ShowStartupPrompt);
    }

    /// <summary>
    /// Confirms current versions only after a successful check and clears success after a failed manual retry.
    /// </summary>
    [Fact]
    public async Task CurrentReleaseStaysQuietAndFailedRetryClearsConfirmation()
    {
        using var fixture = new TrackerUpdateTestFixture { ObservedVersion = SignedReleaseFixture.VersionB };
        await fixture.Session.StartAsync();
        Assert.False(fixture.Session.ShowStartupPrompt);
        Assert.Null(fixture.Session.AvailableVersion);
        Assert.NotNull(fixture.Session.CheckedAt);
        Assert.Equal(fixture.Release.Manifest.Game.VersionLabel, fixture.Session.CurrentGameVersion);
        fixture.Offline = true;
        await fixture.Session.OpenFromSettingsAsync();
        Assert.True(fixture.Session.OpenedFromSettings);
        Assert.NotNull(fixture.Session.Error);
        Assert.Null(fixture.Session.CheckedAt);
    }

    /// <summary>
    /// Never substitutes the target game version for a different installed revision.
    /// </summary>
    [Fact]
    public async Task DifferentGameRevisionHasNoInventedVersionLabel()
    {
        using var fixture = new TrackerUpdateTestFixture { ObservedGameCommit = new string('2', 40) };
        await fixture.Session.StartAsync();
        Assert.Null(fixture.Session.CurrentGameVersion);
    }

    /// <summary>
    /// Queues a requested manual refresh behind startup discovery and keeps the invitation closed.
    /// </summary>
    [Fact]
    public async Task SettingsDuringStartupPerformsAFreshCheck()
    {
        using var fixture = new TrackerUpdateTestFixture { MetadataGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var startup = fixture.Session.StartAsync();
        await fixture.MetadataStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.Session.OpenFromSettingsAsync();
        fixture.MetadataGate.SetResult();
        await startup.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(6, fixture.MetadataRequests);
        Assert.True(fixture.Session.OpenedFromSettings);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        Assert.False(fixture.Session.ShowStartupPrompt);
    }

    /// <summary>
    /// Runs the ordinary two-action flow through real preparation while deferring all replacement until independent handoff.
    /// </summary>
    [Fact]
    public async Task UpdatePreparesThenHandsOffWithoutReplacingTheRunningTracker()
    {
        using var fixture = new TrackerUpdateTestFixture { ActiveRun = true };
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        await fixture.Session.OpenAsync();
        await fixture.Session.UpdateAsync();
        Assert.Null(fixture.Session.Error);
        var prepared = Assert.IsType<PreparedIronmonUpdate>(fixture.Handoff);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
        Assert.Equal(prepared.TransactionId, UpdateTransaction.ReadActiveId(fixture.Release.Root));
        var engine = new UpdateTransaction(new SignedIronmonAuthority(fixture.Release.Verifier, SignedReleaseFixture.Runtime()), _ => Task.CompletedTask);
        var result = await engine.ApplyAsync(fixture.Release.Root, prepared.TransactionId);
        Assert.Equal(TransactionPhase.Committed, result.Phase);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.NotNull(fixture.Preparation.ReadCurrentRelease(fixture.Release.Root, SignedReleaseFixture.VersionB));
    }

    /// <summary>
    /// Requires exact replacement consent and rejects edits made after that consent during downloads.
    /// </summary>
    [Fact]
    public async Task ConflictConsentDoesNotAuthorizeLaterLocalEdits()
    {
        using var fixture = new TrackerUpdateTestFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script), LocalEdit);
        await fixture.Session.OpenAsync();
        Assert.False(fixture.Session.CanUpdate);
        fixture.Session.Approve(SignedReleaseFixture.Script, true);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        fixture.DuringArchive = () => File.WriteAllText(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script), LaterEdit);
        await fixture.Session.UpdateAsync();
        Assert.NotNull(fixture.Session.Error);
        Assert.Null(fixture.Handoff);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.Equal(LaterEdit, await File.ReadAllTextAsync(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script)));
    }

    /// <summary>
    /// Preserves explicit deleted-file consent as absence, not a fabricated local fingerprint.
    /// </summary>
    [Fact]
    public async Task DeletedFileCanBeReviewedAndRestored()
    {
        using var fixture = new TrackerUpdateTestFixture();
        File.Delete(Path.Combine(fixture.Release.Root, SignedReleaseFixture.Script));
        await fixture.Session.OpenAsync();
        fixture.Session.Approve(SignedReleaseFixture.Script, true);
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
        await fixture.Session.UpdateAsync();
        Assert.Null(fixture.Session.Error);
        Assert.NotNull(fixture.Handoff);
    }

    /// <summary>
    /// Cancels a real in-flight package request without losing the review or leaving a pending transaction.
    /// </summary>
    [Fact]
    public async Task CancellationAllowsRetryWithoutChangingInstalledFiles()
    {
        using var fixture = new TrackerUpdateTestFixture { ArchiveGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        await fixture.Session.OpenAsync();
        var update = fixture.Session.UpdateAsync();
        await fixture.ArchiveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        fixture.Session.Close();
        Assert.True(fixture.Session.IsOpen);
        fixture.Session.Cancel();
        await update.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(TrackerUpdatePhase.Cancelled, fixture.Session.Phase);
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        fixture.ArchiveGate = null;
        await fixture.Session.UpdateAsync();
        Assert.NotNull(fixture.Handoff);
    }

    /// <summary>
    /// Releases only unstarted preparation after helper failure and allows the player to retry from the same tracker.
    /// </summary>
    [Fact]
    public async Task FailedHandoffReleasesPreparedTransactionForRetry()
    {
        using var fixture = new TrackerUpdateTestFixture { FailHandoff = true };
        var before = await InstallationFileSnapshot.ReadAsync(fixture.Release.Root);
        await fixture.Session.OpenAsync();
        await fixture.Session.UpdateAsync();
        Assert.NotNull(fixture.Session.Error);
        Assert.Null(UpdateTransaction.ReadActiveId(fixture.Release.Root));
        Assert.Equal(before, await InstallationFileSnapshot.ReadAsync(fixture.Release.Root));
        fixture.FailHandoff = false;
        await fixture.Session.CheckAsync();
        await fixture.Session.UpdateAsync();
        Assert.NotNull(fixture.Handoff);
    }

    /// <summary>
    /// Keeps discovery failure nonfatal and visible when the player opens the new update interface.
    /// </summary>
    [Fact]
    public async Task OfflineStartupRemainsQuietAndCanBeRetried()
    {
        using var fixture = new TrackerUpdateTestFixture { Offline = true };
        await fixture.Session.StartAsync();
        Assert.False(fixture.Session.IsOpen);
        Assert.NotNull(fixture.Session.Error);
        await fixture.Session.OpenAsync();
        Assert.NotNull(fixture.Session.Error);
        fixture.Offline = false;
        await fixture.Session.CheckAsync();
        Assert.True(fixture.Session.CanUpdate, fixture.Session.Error);
    }

    /// <summary>
    /// Offers authenticated recovery for a pending transaction and refuses to prepare a second update.
    /// </summary>
    [Fact]
    public async Task PendingTransactionUsesRecoveryHandoff()
    {
        using var fixture = new TrackerUpdateTestFixture();
        var pending = await fixture.Release.Coordinator().PrepareAsync(fixture.Release.Request(), fixture.Release.Evidence);
        await fixture.Session.StartAsync();
        Assert.True(fixture.Session.NeedsRecovery);
        Assert.True(fixture.Session.ShowStartupPrompt);
        Assert.False(fixture.Session.CanUpdate);
        Assert.Equal(pending.TransactionId, fixture.Session.Recovery?.TransactionId);
        await fixture.Session.RecoverAsync();
        Assert.True(fixture.RecoveryHandoff);
        Assert.Equal(pending, fixture.Handoff);
    }

    /// <summary>
    /// Does not erase a transaction whose application has already written durable intent.
    /// </summary>
    [Fact]
    public async Task DiscardRejectsStartedTransactions()
    {
        using var fixture = new TrackerUpdateTestFixture();
        var pending = await fixture.Release.Coordinator().PrepareAsync(fixture.Release.Request(), fixture.Release.Evidence);
        var directory = Path.Combine(fixture.Release.Root, InstallationLease.StateDirectory, TransactionStorage.TransactionsDirectory, pending.TransactionId.ToString(TransactionStorage.GuidFormat));
        var journal = TransactionStorage.ReadJournal(directory);
        TransactionStorage.WriteJournal(directory, journal with { Phase = TransactionPhase.Applying, Cursor = 0 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Preparation.DiscardAsync(pending));
        Assert.Equal(pending.TransactionId, UpdateTransaction.ReadActiveId(fixture.Release.Root));
    }
}
