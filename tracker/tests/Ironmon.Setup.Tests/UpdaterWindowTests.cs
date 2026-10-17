using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Ironmon.Updater;
using Ironmon.Updater.Core;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Setup.Tests;

/// <summary>
/// Checks the independent helper's terminal native-window behavior without installing files or launching applications.
/// </summary>
public sealed class UpdaterWindowTests
{
    /// <summary>
    /// Closes a committed update automatically after the tracker has been started.
    /// </summary>
    [Fact]
    public void SuccessfulHandoffClosesTheHelper()
    {
        RunSta(() =>
        {
            var window = new UpdaterWindow([], new UnusedAuthority());
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.CompleteOperation(new TransactionResult(TransactionPhase.Committed), trackerRestarted: true);
            Assert.True(closed);
        });
    }

    /// <summary>
    /// Leaves launch failures, rollback, standalone recovery, cancellation, and unexpected errors available for manual dismissal.
    /// </summary>
    /// <param name="phase">The durable outcome, or null when the operation did not produce a result.</param>
    /// <param name="trackerRestarted">Whether the tracker launch completed.</param>
    /// <param name="installationError">An optional update warning or failure.</param>
    /// <param name="relaunchError">An optional tracker launch failure.</param>
    [Theory]
    [InlineData(TransactionPhase.Committed, false, null, null)]
    [InlineData(TransactionPhase.Committed, false, null, "Tracker could not start.")]
    [InlineData(TransactionPhase.Committed, true, "An update warning needs attention.", null)]
    [InlineData(TransactionPhase.Committed, true, null, "Tracker could not start.")]
    [InlineData(TransactionPhase.RolledBack, true, "The previous installation was restored.", null)]
    [InlineData(null, false, null, null)]
    public void OtherOutcomesKeepAManuallyClosableWindow(TransactionPhase? phase, bool trackerRestarted, string? installationError, string? relaunchError)
    {
        RunSta(() =>
        {
            var window = new UpdaterWindow([], new UnusedAuthority());
            var closed = false;
            window.Closed += (_, _) => closed = true;
            var result = phase is { } value ? new TransactionResult(value, installationError, relaunchError) : null;
            window.CompleteOperation(result, trackerRestarted);
            Assert.False(closed);
            var content = Assert.IsType<StackPanel>(window.Content);
            var action = Assert.Single(content.Children.OfType<Button>());
            Assert.Equal(UpdaterText.UpdaterWindowClose, action.Content);
            Assert.True(action.IsEnabled);
            Assert.False(Assert.Single(content.Children.OfType<ProgressBar>()).IsIndeterminate);
            action.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(closed);
        });
    }

    /// <summary>
    /// Preserves the recovery action instead of closing an installation that still needs restoration.
    /// </summary>
    [Fact]
    public void FailedRecoveryKeepsItsRetryAction()
    {
        RunSta(() =>
        {
            var window = new UpdaterWindow([], new UnusedAuthority());
            var closed = false;
            window.Closed += (_, _) => closed = true;
            window.CompleteOperation(new TransactionResult(TransactionPhase.RecoveryRequired, "Recovery needs attention."), trackerRestarted: false);
            Assert.False(closed);
            var content = Assert.IsType<StackPanel>(window.Content);
            var action = Assert.Single(content.Children.OfType<Button>());
            Assert.Equal(UpdaterText.UpdaterWindowRetryRecovery, action.Content);
            Assert.True(action.IsEnabled);
            window.Close();
            Assert.True(closed);
        });
    }

    /// <summary>
    /// Runs one native-window fixture on an owned STA thread without showing an external desktop window.
    /// </summary>
    /// <param name="action">The native-window assertions.</param>
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    /// <summary>
    /// Rejects any accidental transaction execution from the isolated completion-window fixtures.
    /// </summary>
    private sealed class UnusedAuthority : ITransactionAuthority
    {
        /// <summary>
        /// Blocks preparation, installation, and recovery in a window-only fixture.
        /// </summary>
        /// <param name="description">The unused transaction descriptor.</param>
        /// <param name="exactBytes">The unused authority bytes.</param>
        /// <param name="cancellationToken">The unused operation token.</param>
        /// <returns>No transaction authorization is permitted.</returns>
        public Task AuthorizeAsync(TransactionDescription description, ReadOnlyMemory<byte> exactBytes, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Completion-window fixtures cannot authorize updates.");

        /// <summary>
        /// Blocks inspection of an installation from a window-only fixture.
        /// </summary>
        /// <param name="description">The unused transaction descriptor.</param>
        /// <param name="cancellationToken">The unused verification token.</param>
        /// <returns>No installation verification is permitted.</returns>
        public Task VerifyInstalledAsync(TransactionDescription description, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Completion-window fixtures cannot inspect installations.");
    }
}
