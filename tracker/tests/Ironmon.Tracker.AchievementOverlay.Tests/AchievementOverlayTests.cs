using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Connection.Transport;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Ironmon.Tracker.AchievementOverlay.Tests;

/// <summary>
/// Verifies actual-resolution rendering and native window behavior for the isolated one-icon prototype.
/// </summary>
public sealed class AchievementOverlayTests
{
    private const string _evidenceVariable = "IRONMON_OVERLAY_PREVIEW_TEST_OUTPUT";
    private const string _sharpName = "sharp-{0}.png";
    private const string _originalName = "original-{0}.png";
    private static readonly int[] int32Array = new[] { 512, 768, 1024 };
    private static readonly int[] int32Array0 = new[] { 512, 768, 1024 };

    /// <summary>
    /// Reads installed titles exclusively inside the offline renderer test assembly.
    /// </summary>
    /// <returns>The installed identities and display titles in catalogue order.</returns>
    private static (Guid Id, int Number, string Name)[] ReadInstalledAchievements()
    {
        AchievementPack pack = new();
        HashSet<Guid> ids = [.. pack.Nodes.Select(node => node.Id)];
        return [.. ids.Select(id => (Id: id, Rule: pack.Open(id, ids))).OrderBy(item => item.Rule.Number).Select(item => (item.Id, item.Rule.Number, item.Rule.Name))];
    }

    /// <summary>
    /// Keeps the approved entry, hold and exit phases deterministic under the game-supplied clock.
    /// </summary>
    /// <param name="elapsed">The game-supplied elapsed time.</param>
    /// <param name="expected">The expected visible slide fraction.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.09, 0.875)]
    [InlineData(0.18, 1)]
    [InlineData(2.6, 1)]
    [InlineData(2.87, 0.125)]
    [InlineData(2.96, 0)]
    public void GameClockDefinesEveryAnimationPhase(double elapsed, double expected)
    {
        Assert.Equal(expected, AchievementOverlayPresenter.SlideFraction(elapsed), 8);
    }

    /// <summary>
    /// Keeps the visual size aligned with the game's scale instead of enlarging a fixed 50-pixel buffer.
    /// </summary>
    /// <param name="clientWidth">The actual client width.</param>
    /// <param name="clientHeight">The actual client height.</param>
    /// <param name="iconPixels">The required icon width and height on screen.</param>
    /// <param name="panelWidth">The required panel width on screen.</param>
    /// <param name="margin">The required panel margin on screen.</param>
    [Theory]
    [InlineData(256, 192, 25, 174, 4)]
    [InlineData(512, 384, 50, 348, 8)]
    [InlineData(768, 576, 75, 522, 12)]
    [InlineData(1024, 768, 100, 696, 16)]
    public void PlacementUsesActualGameClientPixels(int clientWidth, int clientHeight, int iconPixels, int panelWidth, int margin)
    {
        OverlayPlacement result = OverlayPlacement.FromClient(-2000, 100, clientWidth, clientHeight);
        Assert.Equal(-2000, result.X);
        Assert.Equal(100 + margin, result.Y);
        Assert.Equal(iconPixels, result.IconPixels);
        Assert.Equal(panelWidth, result.Width);
        Assert.Equal(64 * result.Scale, result.Height);
    }

    /// <summary>
    /// Accounts for title bars being excluded and for the game's aspect-preserving letterbox margins.
    /// </summary>
    [Fact]
    public void PlacementCentersWithinLetterboxedViewport()
    {
        OverlayPlacement result = OverlayPlacement.FromClient(200, 300, 1200, 768);
        Assert.Equal(288, result.X);
        Assert.Equal(316, result.Y);
        Assert.Equal(100, result.IconPixels);
        Assert.Throws<ArgumentOutOfRangeException>(() => OverlayPlacement.FromClient(0, 0, 0, 768));
    }

    /// <summary>
    /// Guards the real notification layout against catalogue names losing a third line behind ellipsis.
    /// </summary>
    /// <returns>A task completing after every installed title fits the unchanged two-line title area.</returns>
    [Fact]
    public async Task CompleteCatalogueTitlesFitWithoutTruncation()
    {
        var catalogue = ReadInstalledAchievements();
        await RunStaAsync(() =>
        {
            using OverlayLifetime overlay = new(new OverlayWindow(0));
            TextBlock title = Assert.IsType<TextBlock>(overlay.Window.FindName(nameof(OverlayWindow.AchievementTitle)));
            double allowedHeight = title.MaxHeight;
            double reservedWidth = title.Margin.Left + title.Margin.Right;
            title.Margin = new Thickness(0);
            title.MaxHeight = double.PositiveInfinity;
            title.TextTrimming = TextTrimming.None;
            foreach (int clientWidth in int32Array)
            {
                OverlayPlacement placement = OverlayPlacement.FromClient(0, 0, clientWidth, clientWidth * 3 / 4);
                double textWidth = placement.Width / placement.Scale - 8 - reservedWidth;
                foreach (var (Id, Number, Name) in catalogue)
                {
                    title.Text = Name;
                    title.Measure(new Size(textWidth, double.PositiveInfinity));
                    Assert.True(title.DesiredSize.Height <= allowedHeight, $"Achievement #{Number} needs more than two lines: {Name}");
                }
            }
        });
    }

    /// <summary>
    /// Loads only two frozen image sources for the explicit comparison without needing an earned ledger.
    /// </summary>
    /// <returns>A task completing after the installed example and per-size source choice are checked.</returns>
    [Fact]
    public async Task ApprovedExampleUsesDetailOnlyWhenAdditionalPixelsAreNeeded()
    {
        OverlayArtwork artwork = await LoadArtworkAsync();
        Assert.Equal("The House Always Crits", artwork.Title);
        Assert.Equal(50, artwork.Small.PixelWidth);
        Assert.NotNull(artwork.Detail);
        Assert.Equal(1024, artwork.Detail.PixelWidth);
        Assert.True(artwork.Small.IsFrozen && artwork.Detail.IsFrozen);
        Assert.Same(artwork.Small, artwork.Select(25, false));
        Assert.Same(artwork.Small, artwork.Select(50, false));
        Assert.Same(artwork.Detail, artwork.Select(75, false));
        Assert.Same(artwork.Detail, artwork.Select(100, false));
        Assert.Same(artwork.Small, artwork.Select(100, true));
    }

    /// <summary>
    /// Exercises the actual production dispatcher's prepare, visibility, stale-stop, reset and disposal lifecycle.
    /// </summary>
    /// <returns>A task completing after the separate renderer thread has released its native window.</returns>
    [Fact]
    public async Task ProductionPresenterOwnsItsThreadAndReleasesTheCurrentPresentation()
    {
        const string PngPrefix = "data:image/png;base64,";
        var (Id, Number, Name) = ReadInstalledAchievements().Single(item => item.Number == 315);
        var small = AchievementIconPack.Shared.ForEarned(Id);
        var detail = (await AchievementArtworkPack.Shared.ForEarnedAsync(AchievementIconPack.Shared.AssignedIconId(Id)!.Value, CancellationToken.None))!;
        AchievementOverlayPresentation material = new(Guid.NewGuid().ToString(), string.Empty, Name, Convert.FromBase64String(small.ImageDataUri[PngPrefix.Length..]), Convert.FromBase64String(detail.ImageDataUri[PngPrefix.Length..]));
        await RunStaAsync((Func<Task>)(async () =>
        {
            Window fixture = new() { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Left = -22000, Top = -22000, Width = 1024, Height = 768 };
            try
            {
                fixture.Show();
                nint handle = new WindowInteropHelper(fixture).Handle;
                bool visible = false;
                await using AchievementOverlayPresenter presenter = new(_ => handle, _ => Volatile.Read(ref visible));
                using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(8));
                AchievementOverlayState prepared = await presenter.PrepareAsync(material, timeout.Token);
                Assert.True(prepared.Available);
                Assert.False(prepared.Visible);
                visible = true;
                AchievementOverlayState shown = await presenter.UpdateAsync(material.PresentationId, 0.09, timeout.Token);
                Assert.True(shown.Available && shown.Visible);
                presenter.Stop(Guid.NewGuid().ToString());
                await Task.Delay(80, timeout.Token);
                Assert.True((await presenter.UpdateAsync(material.PresentationId, 0.09, timeout.Token)).Available);
                visible = false;
                Assert.False((await presenter.UpdateAsync(material.PresentationId, 0.09, timeout.Token)).Visible);
                presenter.Reset();
                Assert.False((await presenter.UpdateAsync(material.PresentationId, 0.09, timeout.Token)).Available);
            }
            finally
            {
                fixture.Close();
            }
        }));
    }

    /// <summary>
    /// Renders real WPF overlay windows at all requested sizes while preserving foreground focus and click-through styles.
    /// </summary>
    /// <returns>A task completing after native geometry, transparency, source comparison and cleanup are checked.</returns>
    [Fact]
    public async Task NativeOverlayRendersAt50And75And100PixelsWithoutActivating()
    {
        OverlayArtwork artwork = await LoadArtworkAsync();
        await RunStaAsync(() =>
        {
            nint foreground = NativeGameWindow.GetForegroundWindow();
            Window fixture = new() { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowActivated = false, ShowInTaskbar = false, Left = -20000, Top = -20000, Width = 512, Height = 384 };
            nint fixtureHandle = new WindowInteropHelper(fixture).EnsureHandle();
            try
            {
                fixture.Show();
                NativeGameWindow.PlaceOverlay(fixtureHandle, new(-20000, -20000, 512, 384, 50, 1));
                using OverlayLifetime overlay = new(new OverlayWindow(fixtureHandle));
                foreach (int width in int32Array0)
                {
                    int height = width * 3 / 4;
                    NativeGameWindow.PlaceOverlay(fixtureHandle, new(-20000, -20000, width, height, 50, 1));
                    OverlayPlacement placement = Assert.IsType<OverlayPlacement>(NativeGameWindow.Measure(fixtureHandle));
                    overlay.Window.ShowAligned(artwork, placement, false);
                    overlay.Window.UpdateLayout();
                    Assert.Equal(foreground, NativeGameWindow.GetForegroundWindow());
                    Assert.True(NativeGameWindow.HasOverlayStyles(overlay.Window.Handle));
                    Assert.Equal((placement.Width, placement.Height), NativeGameWindow.ClientPixels(overlay.Window.Handle));
                    byte[] sharp = Render(overlay.Window, placement, _sharpName);
                    int offset = ((placement.Height / 2) * placement.Width + (int)Math.Round(78 * placement.Scale)) * 4;
                    Assert.InRange(sharp[offset + 3], 189, 193);
                    overlay.Window.ShowAligned(artwork, placement, true);
                    overlay.Window.UpdateLayout();
                    byte[] original = Render(overlay.Window, placement, _originalName);
                    if (placement.IconPixels == 50)
                        Assert.Equal(sharp, original);
                    else
                        Assert.False(sharp.SequenceEqual(original));

                    overlay.Window.Hide();
                    Assert.False(overlay.Window.IsVisible);
                }

                OverlayPlacement clipped = OverlayPlacement.FromClient(-20000, -20000, 1024, 768);
                overlay.Window.ShowAligned(artwork, clipped, false, 0);
                overlay.Window.UpdateLayout();
                byte[] outside = Render(overlay.Window, clipped, _sharpName);
                Assert.All(Enumerable.Range(0, clipped.Width * clipped.Height), pixel => Assert.Equal(0, outside[pixel * 4 + 3]));
                overlay.Window.ShowAligned(artwork, clipped, false, 0.5);
                overlay.Window.UpdateLayout();
                byte[] halfway = Render(overlay.Window, clipped, _sharpName);
                Assert.Contains(Enumerable.Range(0, clipped.Width * clipped.Height), pixel => halfway[pixel * 4 + 3] != 0);
            }
            finally
            {
                fixture.Close();
            }
        });
    }

    /// <summary>
    /// Supplies the approved encrypted sample through the same authorized image contract as production.
    /// </summary>
    /// <returns>The frozen native image pair.</returns>
    private static async Task<OverlayArtwork> LoadArtworkAsync()
    {
        const string PngPrefix = "data:image/png;base64,";
        var entry = ReadInstalledAchievements().Single(item => item.Number == 315);
        var small = AchievementIconPack.Shared.ForEarned(entry.Id);
        var detail = (await AchievementArtworkPack.Shared.ForEarnedAsync(AchievementIconPack.Shared.AssignedIconId(entry.Id)!.Value, CancellationToken.None))!;
        return OverlayArtwork.FromPresentation(new(Guid.NewGuid().ToString(), string.Empty, entry.Name, Convert.FromBase64String(small.ImageDataUri[PngPrefix.Length..]), Convert.FromBase64String(detail.ImageDataUri[PngPrefix.Length..])));
    }
    /// <summary>
    /// Captures the real renderer at the physical output size and optionally saves visual acceptance artifacts.
    /// </summary>
    /// <param name="window">The live prototype-owned overlay.</param>
    /// <param name="placement">Its physical bounds.</param>
    /// <param name="nameFormat">The evidence filename format.</param>
    /// <returns>The rendered premultiplied BGRA pixels.</returns>
    private static byte[] Render(OverlayWindow window, OverlayPlacement placement, string nameFormat)
    {
        Matrix transform = PresentationSource.FromVisual(window)!.CompositionTarget.TransformToDevice;
        RenderTargetBitmap bitmap = new(placement.Width, placement.Height, 96 * transform.M11, 96 * transform.M22, PixelFormats.Pbgra32);
        bitmap.Render(window);
        byte[] pixels = new byte[placement.Width * placement.Height * 4];
        bitmap.CopyPixels(pixels, placement.Width * 4, 0);
        string? directory = Environment.GetEnvironmentVariable(_evidenceVariable);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
            using Stream stream = File.Create(Path.Combine(directory, string.Format(System.Globalization.CultureInfo.InvariantCulture, nameFormat, placement.IconPixels)));
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        return pixels;
    }

    /// <summary>
    /// Runs native WPF acceptance on an isolated STA thread without changing the user's active application.
    /// </summary>
    /// <param name="check">The real-window acceptance checks.</param>
    /// <returns>A task propagating the result of the STA checks.</returns>
    private static Task RunStaAsync(Action check)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                check();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Pumps a test-owned STA while asynchronous production renderer operations complete on their own dispatcher.
    /// </summary>
    /// <param name="check">The asynchronous native acceptance operation.</param>
    /// <returns>A task propagating the completed native checks.</returns>
    private static Task RunStaAsync(Func<Task> check)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await check();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
                finally
                {
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
                }
            }));
            Dispatcher.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Closes a test-owned native overlay even when a rendering assertion fails.
    /// </summary>
    /// <remarks>
    /// Initializes the owned-window lifetime wrapper.
    /// </remarks>
    /// <param name="window">The native overlay owned by the check.</param>
    private sealed class OverlayLifetime(OverlayWindow window) : IDisposable
    {
        /// <summary>
        /// Gets the test-owned overlay window.
        /// </summary>
        internal OverlayWindow Window { get; } = window;

        /// <summary>
        /// Closes only the overlay created by this acceptance check.
        /// </summary>
        public void Dispose() => Window.Close();
    }
}
