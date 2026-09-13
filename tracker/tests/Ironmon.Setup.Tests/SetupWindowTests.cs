using Ironmon.Setup.Core;
using Ironmon.Updater.Core;
using Ironmon.Tracker.Connection.Sprites;
using Ironmon.Updater.Infrastructure;
using Ironmon.Updater.Tests;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ironmon.Setup.Tests;

/// <summary>
/// Renders real native Setup controls in an isolated STA fixture without showing or operating desktop applications.
/// </summary>
public sealed class SetupWindowTests
{
    private const string Artifacts = "rendered-setup";
    private const string LocationImage = "location.png";
    private const string OptionsImage = "options.png";
    private const string ExistingOptionsImage = "options-existing.png";
    private const string NewDestination = "new-installation";
    private const string UnavailableImage = "release-unavailable.png";
    private const string ConfirmationImage = "run-confirmation.png";
    private const string ReviewImage = "review.png";
    private const string ConflictImage = "conflict.png";
    private const string ReadyImage = "ready.png";
    private const string PageField = "_page";
    private const string RenderMethod = "Render";
    private const string Git = "git";
    private const string Home = "home";
    private const string Staging = "staging";
    private const string GameExe = "InfiniteFusion2.exe";
    private const string GameIni = "Game.ini";
    private const string GitHead = ".git/HEAD";
    private const string Ini = "[Game]\nTitle=infinitefusion-hoenn\n";
    private const string Shortcut = "Ironmon Tracker.lnk";
    private const string Desktop = "desktop";
    private const string Other = "other";
    private const string UnsignedFile = "unsigned.exe";
    private const string ProgressImage = "installation-progress.png";
    private const string BusyField = "_busy";

    /// <summary>
    /// Keeps measured progress and elapsed time visible at the minimum window size without scrolling.
    /// </summary>
    [Fact]
    public async Task BusyInstallationShowsCountsWithoutScrolling()
    {
        using var fixture = new TrackerUpdateTestFixture();
        fixture.Release.Write(GameExe, [77, 90, 0]);
        fixture.Release.Write(GameIni, System.Text.Encoding.UTF8.GetBytes(Ini));
        fixture.Release.Write(GitHead, System.Text.Encoding.UTF8.GetBytes(SignedReleaseFixture.Commit));
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = CreateSession(fixture, sprites);
        await session.ReviewAsync(fixture.Release.Root, null, noActiveRun: true);
        Assert.NotNull(session.Review);
        typeof(SetupSession).GetField(BusyField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, 1);
        var value = new InstallationProgress(InstallationStage.DownloadingGame, 15234, 24243, 1384090310);
        typeof(SetupSession).GetProperty(nameof(SetupSession.Progress))!.SetValue(session, value);
        typeof(SetupSession).GetProperty(nameof(SetupSession.StartedAt))!.SetValue(session, Environment.TickCount64 - 120000);
        typeof(SetupSession).GetProperty(nameof(SetupSession.ProgressAt))!.SetValue(session, Environment.TickCount64);
        RunSta(() =>
        {
            var window = new SetupWindow(session);
            typeof(SetupWindow).GetField(PageField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 2);
            typeof(SetupWindow).GetMethod(RenderMethod, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            Capture(window, ProgressImage);
            var bar = Descendants<ProgressBar>((DependencyObject)window.Content).Single();
            Assert.False(bar.IsIndeterminate);
            Assert.Equal(value.Completed, bar.Value);
            Assert.Equal(value.Total, bar.Maximum);
            Assert.Contains(Descendants<TextBlock>((DependencyObject)window.Content), text => text.Text.Contains(value.Text, StringComparison.Ordinal));
            typeof(SetupSession).GetField(BusyField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, 0);
            window.Close();
        });
    }

    /// <summary>
    /// Verifies native defaults, minimum-size layout, actual button navigation and signed review rendering.
    /// </summary>
    /// <param name="existing">Whether the selected folder already contains an installed tracker.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeLocationOptionsAndReviewKeepOptionalWorkUnchecked(bool existing)
    {
        using var fixture = new TrackerUpdateTestFixture();
        fixture.Release.Write(GameExe, [77, 90, 0]);
        fixture.Release.Write(GameIni, System.Text.Encoding.UTF8.GetBytes(Ini));
        fixture.Release.Write(GitHead, System.Text.Encoding.UTF8.GetBytes(SignedReleaseFixture.Commit));
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = CreateSession(fixture, sprites);
        RunSta(() =>
        {
            var window = new SetupWindow(session);
            Assert.Equal(680, window.MinWidth);
            Assert.Equal(600, window.MinHeight);
            Descendants<TextBox>((DependencyObject)window.Content).Single().Text = existing ? fixture.Release.Root : Path.Combine(fixture.Release.Cache, NewDestination);
            Capture(window, LocationImage);
            Buttons(window).Single(button => Equals(button.Content, "Continue")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => Buttons(window).Any(button => Equals(button.Content, "Review installation")));
            Assert.Null(session.Error);
            CheckBox[] choices = [.. Descendants<CheckBox>((DependencyObject)window.Content)];
            Assert.Equal(3, choices.Length);
            Assert.All(choices, choice => Assert.False(choice.IsChecked));
            Assert.Empty(Descendants<ComboBox>((DependencyObject)window.Content));
            RadioButton[] packages = [.. Descendants<RadioButton>((DependencyObject)window.Content)];
            Assert.Equal(existing ? 0 : 2, packages.Length);
            if (!existing)
                Assert.Single(packages, choice => choice.IsChecked == true);

            choices[0].IsChecked = true;
            Capture(window, existing ? ExistingOptionsImage : OptionsImage);
            window.Close();
        });
        await session.ReviewAsync(fixture.Release.Root, null, true);
        Assert.Null(session.Error);
        RunSta(() =>
        {
            var window = new SetupWindow(session);
            typeof(SetupWindow).GetField(PageField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 2);
            typeof(SetupWindow).GetMethod(RenderMethod, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            Assert.False(Buttons(window).Single(button => Equals(button.Content, "Install")).IsEnabled);
            Capture(window, ReviewImage);
            window.Close();
        });
    }

    /// <summary>
    /// Keeps release failures actionable and presents a run checkbox only for a signed policy that needs one.
    /// </summary>
    /// <param name="confirmation">Whether the release requires a finished run instead of returning a missing-release error.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedReviewAndRunConfirmationHaveDistinctActions(bool confirmation)
    {
        using var fixture = new TrackerUpdateTestFixture { Offline = !confirmation };
        fixture.Release.Write(GameExe, [77, 90, 0]);
        fixture.Release.Write(GameIni, System.Text.Encoding.UTF8.GetBytes(Ini));
        fixture.Release.Write(GitHead, System.Text.Encoding.UTF8.GetBytes(SignedReleaseFixture.Commit));
        if (confirmation)
            fixture.Release.RequireCompletedRun();

        using var sprites = new CustomSpriteSheetInstaller();
        using var session = CreateSession(fixture, sprites);
        await session.SelectDestinationAsync(fixture.Release.Root);
        await session.ReviewAsync(fixture.Release.Root, null, false);
        Assert.NotNull(session.Error);
        RunSta(() =>
        {
            var window = new SetupWindow(session);
            typeof(SetupWindow).GetField(PageField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 2);
            typeof(SetupWindow).GetMethod(RenderMethod, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            Assert.DoesNotContain(Buttons(window), button => Equals(button.Content, "Install"));
            var retry = Buttons(window).Single(button => Equals(button.Content, "Check again"));
            Assert.Equal(!confirmation, retry.IsEnabled);
            CheckBox[] choices = [.. Descendants<CheckBox>((DependencyObject)window.Content)];
            Assert.Equal(confirmation ? 1 : 0, choices.Length);
            Capture(window, confirmation ? ConfirmationImage : UnavailableImage);
            if (confirmation)
            {
                choices[0].IsChecked = true;
                choices[0].RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Assert.True(retry.IsEnabled);
            }

            Buttons(window).Single(button => Equals(button.Content, "Back")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Null(session.Error);
            Assert.False(session.NeedsRunConfirmation);
            Assert.DoesNotContain("Checking", session.Status);
            Assert.Empty(Descendants<RadioButton>((DependencyObject)window.Content));
            Buttons(window).Single(button => Equals(button.Content, "Review installation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PumpUntil(() => !session.IsBusy && Buttons(window).Any(button => Equals(button.Content, "Check again")));
            Assert.Equal(confirmation, session.NeedsRunConfirmation);
            Assert.NotNull(session.Error);
            if (confirmation)
            {
                var consent = Descendants<CheckBox>((DependencyObject)window.Content).Single();
                consent.IsChecked = true;
                consent.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                Buttons(window).Single(button => Equals(button.Content, "Check again")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpUntil(() => !session.IsBusy && Buttons(window).Any(button => Equals(button.Content, "Install")));
                Assert.Null(session.Error);
                Assert.True(session.CanInstall);
            }

            window.Close();
        });
    }

    /// <summary>
    /// Keeps every changed file reachable without scrolling and retains independent approvals across file navigation.
    /// </summary>
    [Fact]
    public async Task ChangedFilesArePagedAndRequireEachApproval()
    {
        using var fixture = new TrackerUpdateTestFixture();
        fixture.Release.Write(GameExe, [77, 90, 0]);
        fixture.Release.Write(GameIni, System.Text.Encoding.UTF8.GetBytes(Ini));
        fixture.Release.Write(GitHead, System.Text.Encoding.UTF8.GetBytes(SignedReleaseFixture.Commit));
        fixture.Release.Write(SignedReleaseFixture.Script, [88]);
        fixture.Release.Write(SignedReleaseFixture.Data, [89]);
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = CreateSession(fixture, sprites);
        await session.ReviewAsync(fixture.Release.Root, null, true);
        Assert.Null(session.Error);
        Assert.False(session.CanInstall);
        RunSta(() =>
        {
            var window = new SetupWindow(session);
            typeof(SetupWindow).GetField(PageField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 2);
            typeof(SetupWindow).GetMethod(RenderMethod, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            Buttons(window).Single(button => Equals(button.Content, UpdaterText.SetupWindowChangedFilesCount(2))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Capture(window, ConflictImage);
            var first = Descendants<CheckBox>((DependencyObject)window.Content).Single();
            first.IsChecked = true;
            first.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert.False(session.CanInstall);
            Buttons(window).Single(button => Equals(button.Content, UpdaterText.SetupWindowNextFile)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var second = Descendants<CheckBox>((DependencyObject)window.Content).Single();
            Assert.False(second.IsChecked);
            second.IsChecked = true;
            second.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            Assert.True(session.CanInstall);
            Buttons(window).Single(button => Equals(button.Content, UpdaterText.SetupWindowPreviousFile)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(Descendants<CheckBox>((DependencyObject)window.Content).Single().IsChecked);
            Buttons(window).Single(button => Equals(button.Content, UpdaterText.SetupWindowBack)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(session.CanInstall);
            Capture(window, ReviewImage);
            window.Close();
        });
    }

    /// <summary>
    /// Fits optional-work failures and completion actions on the ready page without scrolling or displacing buttons.
    /// </summary>
    [Fact]
    public void ReadyPageFitsWithOptionalWorkAndLongErrors()
    {
        using var fixture = new TrackerUpdateTestFixture();
        using var sprites = new CustomSpriteSheetInstaller();
        using var session = CreateSession(fixture, sprites);
        typeof(SetupSession).GetProperty(nameof(SetupSession.CoreInstalled))!.SetValue(session, true);
        typeof(SetupSession).GetProperty(nameof(SetupSession.OptionalIncomplete))!.SetValue(session, true);
        typeof(SetupSession).GetProperty(nameof(SetupSession.Error))!.SetValue(session, string.Concat(Enumerable.Repeat(UpdaterText.SetupWindowOptionalWorkIsIncompleteRetryNowOrFinishAnd, 10)));
        RunSta(() =>
        {
            var window = new SetupWindow(session);
            typeof(SetupWindow).GetField(PageField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, 3);
            typeof(SetupWindow).GetMethod(RenderMethod, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!.Invoke(window, null);
            Capture(window, ReadyImage);
            window.Close();
        });
    }

    /// <summary>
    /// Connects the real installer to signed disposable data and an inert Windows integration boundary.
    /// </summary>
    /// <param name="fixture">The isolated release and filesystem.</param>
    /// <param name="sprites">The shared sprite service, which these render tests do not invoke.</param>
    /// <returns>The installer session used by native interaction tests.</returns>
    private static SetupSession CreateSession(TrackerUpdateTestFixture fixture, CustomSpriteSheetInstaller sprites)
    {
        var downloads = new ReleaseDownloadStore(fixture.Release.Cache, fixture);
        var cache = new MinGitCache(Path.Combine(fixture.Release.Cache, Git), fixture);
        var policy = new RepositoryPolicy(new Uri(ReleaseProtocol.GameRepository), ReleaseProtocol.GameBranch);
        var game = new CombinedGamePreparation(cache, policy, Path.Combine(fixture.Release.Cache, Staging));
        var verification = new CombinedGitVerification(cache, policy, Path.Combine(fixture.Release.Cache, Home));
        var preparation = new SetupPreparation(fixture.Release.Verifier, downloads, SignedReleaseFixture.Runtime(), game, verification, _ => SignedReleaseFixture.VersionA);
        return new SetupSession(fixture.Discovery, preparation, (_, progress) => new UpdateTransaction(new SignedIronmonAuthority(fixture.Release.Verifier, SignedReleaseFixture.Runtime(), verification), _ => Task.CompletedTask, progress), sprites, new Platform(), downloads);
    }

    /// <summary>
    /// Pumps the owned STA dispatcher until asynchronous button navigation finishes without blocking its continuation.
    /// </summary>
    /// <param name="completed">The observable UI completion condition.</param>
    private static void PumpUntil(Func<bool> completed)
    {
        var frame = new DispatcherFrame();
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => frame.Continue = !completed() && elapsed.Elapsed < TimeSpan.FromSeconds(10);
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(completed(), "Native installer navigation did not finish.");
    }

    /// <summary>
    /// Rejects an unsigned downloaded prerequisite using the real Windows publisher verifier without executing the file.
    /// </summary>
    [Fact]
    public async Task UnsignedPrerequisiteIsRejectedBeforeExecution()
    {
        using var workspace = new TestWorkspace();
        var file = workspace.PathFor(UnsignedFile);
        await File.WriteAllBytesAsync(file, [77, 90, 0, 0]);
        await Assert.ThrowsAsync<IOException>(() => WindowsSetupPlatform.VerifyMicrosoftPublisherAsync(file, CancellationToken.None));
    }

    /// <summary>
    /// Creates a real Windows shortcut once and preserves it when another installation requests the same name.
    /// </summary>
    [Fact]
    public void ShortcutCreationIsIdempotentAndPreservesUnrelatedTargets()
    {
        using var fixture = new SignedReleaseFixture();
        using var workspace = new TestWorkspace();
        var desktop = workspace.PathFor(Desktop);
        Directory.CreateDirectory(desktop);
        var other = workspace.PathFor(Other);
        var otherTracker = Path.Combine(other, UpdaterHandoff.TrackerRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(otherTracker)!);
        File.WriteAllBytes(otherTracker, [77, 90, 0, 0]);
        RunSta(() =>
        {
            WindowsSetupPlatform.CreateShortcut(fixture.Root, desktop);
            var original = File.ReadAllBytes(Path.Combine(desktop, Shortcut));
            WindowsSetupPlatform.CreateShortcut(fixture.Root, desktop);
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(desktop, Shortcut)));
            Assert.Throws<IOException>(() => WindowsSetupPlatform.CreateShortcut(other, desktop));
            Assert.Equal(original, File.ReadAllBytes(Path.Combine(desktop, Shortcut)));
            Assert.Single(Directory.GetFiles(desktop));
        });
    }

    /// <summary>
    /// Finds native action controls through the real logical control tree.
    /// </summary>
    /// <param name="window">The isolated native window.</param>
    /// <returns>The native action controls.</returns>
    private static IEnumerable<Button> Buttons(SetupWindow window)
        => Descendants<Button>((DependencyObject)window.Content);

    /// <summary>
    /// Walks the owned logical WPF tree without interacting with external windows.
    /// </summary>
    /// <typeparam name="T">The requested control type.</typeparam>
    /// <param name="root">The owned root.</param>
    /// <returns>The matching controls.</returns>
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match)
            yield return match;

        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }

    /// <summary>
    /// Captures the actual native layout at its minimum supported content size for visual review.
    /// </summary>
    /// <param name="window">The isolated Setup window.</param>
    /// <param name="name">The fixed artifact basename.</param>
    private static void Capture(SetupWindow window, string name)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(664, 561));
        content.Arrange(new Rect(0, 0, 664, 561));
        content.UpdateLayout();
        Assert.Empty(Descendants<ScrollViewer>(content));
        var layout = Assert.IsType<DockPanel>(content);
        var body = Assert.IsType<StackPanel>(layout.Children[2]);
        var footer = Assert.IsType<StackPanel>(layout.Children[1]);
        var footerTop = footer.TransformToAncestor(content).Transform(new Point()).Y;
        foreach (var child in body.Children.OfType<FrameworkElement>())
        {
            var bounds = child.TransformToAncestor(content).TransformBounds(new Rect(child.RenderSize));
            Assert.True(bounds.Bottom <= footerTop, $"Installer content overlaps its footer: {name}, {child.GetType().Name}, {bounds.Bottom} > {footerTop}.");
        }

        foreach (var button in Buttons(window))
        {
            var bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
            Assert.True(bounds.Bottom <= 561 && bounds.Right <= 664, "Installer actions must fit inside the window.");
        }

        var bitmap = new RenderTargetBitmap(664, 561, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var directory = Path.Combine(AppContext.BaseDirectory, Artifacts);
        Directory.CreateDirectory(directory);
        using var output = File.Create(Path.Combine(directory, name));
        encoder.Save(output);
        Assert.True(content.ActualWidth <= 664);
    }

    /// <summary>
    /// Executes native layout work on an owned STA thread and rethrows fixture failures on the test thread.
    /// </summary>
    /// <param name="action">The isolated native fixture.</param>
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
    /// Displays the missing-runtime choice without installing or launching anything on the desktop.
    /// </summary>
    private sealed class Platform : ISetupPlatform
    {
        /// <summary>
        /// Gets the fixture's missing WebView prerequisite.
        /// </summary>
        public bool WebViewAvailable => false;

        /// <summary>
        /// Accepts the isolated native fixture platform.
        /// </summary>
        public void EnsureSupported() { }

        /// <summary>
        /// Rejects accidental prerequisite execution from a rendering fixture.
        /// </summary>
        /// <param name="cancellationToken">The unused fixture token.</param>
        /// <returns>No installation is permitted.</returns>
        public Task InstallWebViewAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("Rendering cannot install prerequisites.");

        /// <summary>
        /// Rejects accidental desktop process operations from a rendering fixture.
        /// </summary>
        /// <param name="root">The unused destination.</param>
        /// <param name="cancellationToken">The unused token.</param>
        /// <returns>No process operation is permitted.</returns>
        public Task CloseInstallationAsync(string root, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Rendering cannot close applications.");

        /// <summary>
        /// Rejects accidental shortcut creation from a rendering fixture.
        /// </summary>
        /// <param name="root">The unused destination.</param>
        public void CreateShortcut(string root)
            => throw new InvalidOperationException("Rendering cannot create shortcuts.");

        /// <summary>
        /// Rejects accidental desktop launches from a rendering fixture.
        /// </summary>
        /// <param name="root">The unused destination.</param>
        public void OpenTracker(string root)
            => throw new InvalidOperationException("Rendering cannot launch applications.");
    }
}
