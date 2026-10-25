using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.AchievementOverlay.Tests;

/// <summary>
/// Verifies the actual native renderer, image quality, window behavior and production message-pump lifecycle.
/// </summary>
public sealed class AchievementOverlayTests
{
    private const string _evidenceVariable = "IRONMON_OVERLAY_PREVIEW_TEST_OUTPUT";
    private const string _sharpName = "sharp-{0}.png";
    private const string _originalName = "original-{0}.png";
    private const string _pngPrefix = "data:image/png;base64,";
    private const string _runtimeOptions = "runtimeOptions";
    private const string _framework = "framework";
    private const string _frameworkName = "name";
    private const string _coreFramework = "Microsoft.NETCore.App";
    private const string _configurationName = "Ironmon.Tracker.AchievementOverlay.Tests.runtimeconfig.json";
    private const string _presentationFramework = "PresentationFramework";
    private const string _presentationCore = "PresentationCore";
    private const string _windowsBase = "WindowsBase";
    private const string _userLibrary = "user32.dll";
    private static readonly int[] _clientWidths = [512, 768, 1024];

    /// <summary>
    /// Keeps animation deterministic under the game-supplied clock.
    /// </summary>
    /// <param name="elapsed">The visible elapsed time in seconds.</param>
    /// <param name="expected">The expected slide fraction.</param>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0.09, 0.875)]
    [InlineData(0.18, 1)]
    [InlineData(2.6, 1)]
    [InlineData(2.87, 0.125)]
    [InlineData(2.96, 0)]
    public void GameClockDefinesEveryAnimationPhase(double elapsed, double expected)
        => Assert.Equal(expected, AchievementOverlayPresenter.SlideFraction(elapsed), 8);

    /// <summary>
    /// Aligns output to the real game viewport rather than enlarging a fixed icon buffer.
    /// </summary>
    /// <param name="clientWidth">The physical game width.</param>
    /// <param name="clientHeight">The physical game height.</param>
    /// <param name="iconPixels">The expected physical icon width.</param>
    /// <param name="panelWidth">The expected physical panel width.</param>
    /// <param name="margin">The expected physical top margin.</param>
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
    /// Preserves aspect-ratio letterboxing and excludes the game's title bar.
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
    /// Prevents the test host and renderer from quietly reacquiring a Windows Desktop framework dependency.
    /// </summary>
    [Fact]
    public void NativeRendererNeedsOnlyTheCoreRuntime()
    {
        using JsonDocument configuration = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, _configurationName)));
        JsonElement requirement = configuration.RootElement.GetProperty(_runtimeOptions).GetProperty(_framework);
        Assert.Equal(_coreFramework, requirement.GetProperty(_frameworkName).GetString());
        Assert.DoesNotContain(typeof(AchievementOverlayPresenter).Assembly.GetReferencedAssemblies(), assembly => assembly.Name is _presentationFramework or _presentationCore or _windowsBase);
    }

    /// <summary>
    /// Guards the real renderer against losing installed achievement titles behind a third line.
    /// </summary>
    [Fact]
    public void CompleteCatalogueTitlesFitWithoutTruncation()
    {
        foreach (var entry in ReadInstalledAchievements())
            Assert.True(OverlayCanvas.MeasureTitle(entry.Name).Length <= 2, $"Achievement #{entry.Number} needs more than two lines: {entry.Name}");
    }

    /// <summary>
    /// Preserves all characters when a future title contains an individually oversized word.
    /// </summary>
    [Fact]
    public void OversizedTitlesWrapWithoutDroppingText()
    {
        const string title = "AnExtremelyLongAchievementNameWithoutAnySpacesAndMoreLetters";
        string[] lines = OverlayCanvas.MeasureTitle(title);
        Assert.True(lines.Length > 2);
        Assert.Equal(title, string.Concat(lines));
    }

    /// <summary>
    /// Selects high-resolution artwork only when more than fifty source pixels are needed.
    /// </summary>
    /// <returns>A task completing after the installed approved image pair is checked.</returns>
    [Fact]
    public async Task ApprovedExampleUsesDetailOnlyWhenAdditionalPixelsAreNeeded()
    {
        using OverlayArtwork artwork = OverlayArtwork.FromPresentation(await LoadPresentationAsync());
        Assert.Equal("The House Always Crits", artwork.Title);
        Assert.Equal(50, artwork.Small.Width);
        Assert.NotNull(artwork.Detail);
        Assert.Equal(1024, artwork.Detail.Width);
        Assert.Same(artwork.Small, artwork.Select(25, false));
        Assert.Same(artwork.Small, artwork.Select(50, false));
        Assert.Same(artwork.Detail, artwork.Select(75, false));
        Assert.Same(artwork.Detail, artwork.Select(100, false));
        Assert.Same(artwork.Small, artwork.Select(100, true));
    }

    /// <summary>
    /// Exercises actual preparation, pause, visibility, stale stops, replacement, reset and renderer shutdown.
    /// </summary>
    /// <returns>A task completing after all owned native resources have been released.</returns>
    [Fact]
    public async Task ProductionPresenterOwnsItsThreadAndReleasesTheCurrentPresentation()
    {
        AchievementOverlayPresentation material = await LoadPresentationAsync();
        await using NativeFixture fixture = await NativeFixture.CreateAsync();
        bool visible = false;
        await using AchievementOverlayPresenter presenter = new(_ => fixture.Handle, _ => Volatile.Read(ref visible));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        AchievementOverlayState prepared = await presenter.PrepareAsync(material, timeout.Token);
        Assert.True(prepared.Available);
        Assert.False(prepared.Visible);
        visible = true;
        AchievementOverlayState shown = await presenter.UpdateAsync(material.PresentationId, 0.09, timeout.Token);
        Assert.True(shown.Available && shown.Visible);
        await Task.Delay(80, timeout.Token);
        Assert.Equal(shown, await presenter.UpdateAsync(material.PresentationId, 0.09, timeout.Token));
        AchievementOverlayPresentation replacement = material with { PresentationId = Guid.NewGuid().ToString() };
        Assert.True((await presenter.PrepareAsync(replacement, timeout.Token)).Available);
        presenter.Stop(material.PresentationId);
        Assert.True((await presenter.UpdateAsync(replacement.PresentationId, 0.18, timeout.Token)).Available);
        visible = false;
        Assert.False((await presenter.UpdateAsync(replacement.PresentationId, 0.18, timeout.Token)).Visible);
        presenter.Reset();
        Assert.False((await presenter.UpdateAsync(replacement.PresentationId, 0.18, timeout.Token)).Available);
        await presenter.DisposeAsync();
        Assert.False((await presenter.UpdateAsync(replacement.PresentationId, 0.18, timeout.Token)).Available);
    }

    /// <summary>
    /// Prevents canceled image preparation from creating a notification that outlives its caller.
    /// </summary>
    /// <returns>A task completing after cancellation, invalid data and recovery are checked.</returns>
    [Fact]
    public async Task CanceledOrMalformedPreparationCanRecoverWithoutAnOrphanedWindow()
    {
        AchievementOverlayPresentation material = await LoadPresentationAsync();
        await using NativeFixture fixture = await NativeFixture.CreateAsync();
        await using AchievementOverlayPresenter presenter = new(_ => fixture.Handle, _ => true);
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => presenter.PrepareAsync(material, canceled.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => presenter.PrepareAsync(material with { SmallPng = [1, 2, 3] }, CancellationToken.None));
        Assert.True((await presenter.PrepareAsync(material, CancellationToken.None)).Available);
        presenter.Stop(material.PresentationId);
        Assert.False((await presenter.UpdateAsync(material.PresentationId, 0.18, CancellationToken.None)).Available);
    }

    /// <summary>
    /// Renders real layered windows at all supported sizes, preserving alpha, focus, styles and slide clipping.
    /// </summary>
    /// <returns>A task completing after pixel and native-window acceptance.</returns>
    [Fact]
    public async Task NativeOverlayRendersAt50And75And100PixelsWithoutActivating()
    {
        using OverlayArtwork artwork = OverlayArtwork.FromPresentation(await LoadPresentationAsync());
        await RunNativeAsync(() =>
        {
            nint foreground = NativeGameWindow.GetForegroundWindow();
            nint fixture = OverlayNative.CreateWindow(0);
            nint overlayHandle = 0;
            try
            {
                using OverlayWindow overlay = new(fixture);
                overlayHandle = overlay.Handle;
                foreach (int width in _clientWidths)
                {
                    NativeGameWindow.PlaceOverlay(fixture, new(-20000, -20000, width, width * 3 / 4, 50, 1));
                    OverlayPlacement placement = Assert.IsType<OverlayPlacement>(NativeGameWindow.Measure(fixture));
                    overlay.ShowAligned(artwork, placement, false);
                    Assert.Equal(foreground, NativeGameWindow.GetForegroundWindow());
                    Assert.True(NativeGameWindow.HasOverlayStyles(overlay.Handle));
                    Assert.Equal((placement.Width, placement.Height), NativeGameWindow.ClientPixels(overlay.Handle));
                    byte[] sharp = Capture(overlay.Frame, placement, _sharpName);
                    int offset = ((placement.Height / 2) * placement.Width + (int)Math.Round(78 * placement.Scale)) * 4;
                    Assert.InRange(sharp[offset + 3], 189, 193);
                    overlay.ShowAligned(artwork, placement, true);
                    byte[] original = Capture(overlay.Frame, placement, _originalName);
                    if (placement.IconPixels == 50)
                        Assert.Equal(sharp, original);
                    else
                        Assert.False(sharp.SequenceEqual(original));

                    overlay.Hide();
                    Assert.False(overlay.IsVisible);
                }

                OverlayPlacement clipped = OverlayPlacement.FromClient(-20000, -20000, 1024, 768);
                overlay.ShowAligned(artwork, clipped, false, 0);
                byte[] outside = Capture(overlay.Frame, clipped);
                Assert.All(Enumerable.Range(0, clipped.Width * clipped.Height), pixel => Assert.Equal(0, outside[pixel * 4 + 3]));
                overlay.ShowAligned(artwork, clipped, false, 0.5);
                byte[] halfway = Capture(overlay.Frame, clipped);
                Assert.Contains(Enumerable.Range(0, clipped.Width * clipped.Height), pixel => halfway[pixel * 4 + 3] != 0);
                overlay.ShowAligned(artwork, clipped, false, 0.5);
                Assert.Equal(halfway, Capture(overlay.Frame, clipped));
            }
            finally
            {
                OverlayNative.DestroyWindow(fixture);
            }

            Assert.False(NativeGameWindow.IsWindow(overlayHandle));
        });
    }

    /// <summary>
    /// Repeatedly resizes and closes real native surfaces without accumulating GDI resources.
    /// </summary>
    /// <returns>A task completing after the native drawing resource count returns to its warmed baseline.</returns>
    [Fact]
    public async Task RepeatedNativeResizingReleasesDrawingResources()
    {
        using OverlayArtwork artwork = OverlayArtwork.FromPresentation(await LoadPresentationAsync());
        await RunNativeAsync(() =>
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetCurrentProcess();
            using (OverlayWindow warmup = new(0))
                warmup.ShowAligned(artwork, OverlayPlacement.FromClient(-22000, -22000, 1024, 768), false);

            uint before = GetGuiResources(process.Handle, 0);
            for (int cycle = 0; cycle < 20; cycle++)
            {
                using OverlayWindow overlay = new(0);
                foreach (int width in _clientWidths)
                    overlay.ShowAligned(artwork, OverlayPlacement.FromClient(-22000, -22000, width, width * 3 / 4), false);
            }

            uint after = GetGuiResources(process.Handle, 0);
            Assert.InRange(after, 0u, before + 2);
        });
    }

    /// <summary>
    /// Reads the process's current GDI resource count without modifying any window.
    /// </summary>
    /// <param name="process">The test host's process handle.</param>
    /// <param name="flags">Zero for GDI resources.</param>
    /// <returns>The currently allocated object count.</returns>
    [DllImport(_userLibrary)]
    private static extern uint GetGuiResources(nint process, uint flags);

    /// <summary>
    /// Reads installed titles only inside the offline acceptance assembly.
    /// </summary>
    /// <returns>The stable identities, numbers and approved titles.</returns>
    private static (Guid Id, int Number, string Name)[] ReadInstalledAchievements()
    {
        AchievementPack pack = new();
        HashSet<Guid> ids = [.. pack.Nodes.Select(node => node.Id)];
        return [.. ids.Select(id => (Id: id, Rule: pack.Open(id, ids))).OrderBy(item => item.Rule.Number).Select(item => (item.Id, item.Rule.Number, item.Rule.Name))];
    }

    /// <summary>
    /// Supplies the installed approved example through the same image contract as production.
    /// </summary>
    /// <returns>The in-memory presentation without altering an earned ledger.</returns>
    private static async Task<AchievementOverlayPresentation> LoadPresentationAsync()
    {
        var entry = ReadInstalledAchievements().Single(item => item.Number == 315);
        var small = AchievementIconPack.Shared.ForEarned(entry.Id);
        var detail = (await AchievementArtworkPack.Shared.ForEarnedAsync(AchievementIconPack.Shared.AssignedIconId(entry.Id)!.Value, CancellationToken.None))!;
        return new(Guid.NewGuid().ToString(), string.Empty, entry.Name, Convert.FromBase64String(small.ImageDataUri[_pngPrefix.Length..]), Convert.FromBase64String(detail.ImageDataUri[_pngPrefix.Length..]));
    }

    /// <summary>
    /// Reads the actual submitted physical pixels and optionally writes review images.
    /// </summary>
    /// <param name="bitmap">The submitted native surface.</param>
    /// <param name="placement">The physical output size.</param>
    /// <param name="nameFormat">The optional evidence filename format.</param>
    /// <returns>The premultiplied BGRA frame.</returns>
    private static byte[] Capture(Bitmap bitmap, OverlayPlacement placement, string? nameFormat = null)
    {
        byte[] pixels = new byte[placement.Width * placement.Height * 4];
        BitmapData data = bitmap.LockBits(new(0, 0, placement.Width, placement.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (int row = 0; row < placement.Height; row++)
                Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * placement.Width * 4, placement.Width * 4);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        string? directory = Environment.GetEnvironmentVariable(_evidenceVariable);
        if (directory is not null && nameFormat is not null)
        {
            Directory.CreateDirectory(directory);
            bitmap.Save(Path.Combine(directory, string.Format(System.Globalization.CultureInfo.InvariantCulture, nameFormat, placement.IconPixels)), ImageFormat.Png);
        }

        return pixels;
    }

    /// <summary>
    /// Runs real-window acceptance on an owned physical-coordinate thread without changing the user's application.
    /// </summary>
    /// <param name="check">The synchronous native-window acceptance.</param>
    /// <returns>A task propagating its result.</returns>
    private static Task RunNativeAsync(Action check)
    {
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread thread = new(() =>
        {
            try
            {
                OverlayNative.InitializeThread();
                check();
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }) { IsBackground = true };
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Keeps a disposable game-sized fixture on its owning native thread during asynchronous presenter checks.
    /// </summary>
    /// <remarks>
    /// Initializes the queue, owned window and orderly completion signal.
    /// </remarks>
    /// <param name="dispatcher">The fixture's own native queue.</param>
    /// <param name="handle">The fixture-owned window.</param>
    /// <param name="finished">The owning thread's completion signal.</param>
    private sealed class NativeFixture(OverlayDispatcher dispatcher, nint handle, Task finished) : IAsyncDisposable
    {
        /// <summary>
        /// Gets the test-owned native game window.
        /// </summary>
        internal nint Handle { get; } = handle;

        /// <summary>
        /// Creates an offscreen fixture without WPF or a desktop-framework test host.
        /// </summary>
        /// <returns>The live fixture with an initialized message queue.</returns>
        internal static Task<NativeFixture> CreateAsync()
        {
            TaskCompletionSource<NativeFixture> ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Thread thread = new(() =>
            {
                nint window = 0;
                try
                {
                    OverlayDispatcher queue = new();
                    window = OverlayNative.CreateWindow(0);
                    NativeGameWindow.PlaceOverlay(window, new(-22000, -22000, 1024, 768, 100, 2));
                    ready.TrySetResult(new(queue, window, finished.Task));
                    queue.Run(() => { });
                }
                catch (Exception error)
                {
                    ready.TrySetException(error);
                }
                finally
                {
                    if (window != 0)
                        OverlayNative.DestroyWindow(window);

                    finished.TrySetResult();
                }
            }) { IsBackground = true };
            thread.Start();
            return ready.Task;
        }

        /// <summary>
        /// Stops only the fixture's queue and waits for its native window to be destroyed.
        /// </summary>
        /// <returns>A task completing after the owned thread exits.</returns>
        public async ValueTask DisposeAsync()
        {
            dispatcher.Shutdown();
            await finished.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(NativeGameWindow.IsWindow(Handle));
        }
    }
}
