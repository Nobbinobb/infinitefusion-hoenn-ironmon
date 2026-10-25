using Ironmon.Tracker.Connection.Wardrobe;
using Ironmon.Tracker.Protocol.Wardrobe;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Loads bounded native wardrobe images and cancels obsolete browsing work.
/// </summary>
public partial class WardrobeDialog
{
    /// <summary>
    /// Schedules previews for the next useful render.
    /// </summary>
    private void QueueImages()
    {
        CancelImages();
        _refreshImages = Connected && _state.Open;
        StateHasChanged();
    }

    /// <summary>
    /// Cancels old preview requests before another appearance or page is selected.
    /// </summary>
    private void CancelImages()
    {
        _imageGeneration++;
        _images?.Cancel();
        _images?.Dispose();
        _images = null;
    }

    /// <summary>
    /// Loads the assembled native preview first, followed only by currently visible thumbnails.
    /// </summary>
    /// <returns>Completion after this bounded preview job ends.</returns>
    private async Task LoadImagesAsync()
    {
        CancelImages();
        _images = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        CancellationToken token = _images.Token;
        int generation = _imageGeneration;
        WardrobeAppearancePayload appearance = Appearance;
        if (_imageAppearance != appearance)
        {
            _thumbnails.Clear();
            _profileImages.Clear();
            _mainImage = null;
            _dyeColors = [];
            _baseColors = new Dictionary<int, string>();
        }

        _imageAppearance = appearance;
        try
        {
            WardrobePreviewPayload main = await NativePreviewAsync(appearance, _view, null, null, false, PaletteField, token);
            if (!ImagesCurrent(generation, token))
                return;

            _mainImage = TrackerWardrobeClient.ImageSource(main);
            _baseColors = main.BaseColors;
            _dyeColors = main.DyeColors;
            _skinColors = main.SkinColors;
            _previewError = null;
            await InvokeAsync(StateHasChanged);

            if (_category == WardrobeUi.ProfilesCategory)
            {
                foreach (WardrobeOutfitProfilePayload profile in VisibleProfiles.Where(profile => profile.Available))
                {
                    token.ThrowIfCancellationRequested();
                    WardrobeAppearancePayload saved = profile.Appearance with { SkinTone = appearance.SkinTone };
                    WardrobePreviewPayload image = await NativePreviewAsync(saved, WardrobeProtocol.Portrait, null, profile.Id, false, WardrobeProtocol.HairColor, token);
                    if (!ImagesCurrent(generation, token))
                        return;

                    _profileImages[profile.Id] = TrackerWardrobeClient.ImageSource(image);
                    await InvokeAsync(StateHasChanged);
                }
            }
            else if (_category != WardrobeUi.ColorsCategory)
            {
                foreach (WardrobeCatalogItemPayload item in VisibleItems.Where(item => item.Available))
                {
                    token.ThrowIfCancellationRequested();
                    WardrobePreviewPayload image = await NativePreviewAsync(appearance, WardrobeProtocol.Portrait, item.Key, null, item.Category == WardrobeProtocol.Hair, WardrobeProtocol.HairColor, token);
                    if (!ImagesCurrent(generation, token))
                        return;

                    _thumbnails[item.Key] = TrackerWardrobeClient.ImageSource(image);
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception) when (!ImagesCurrent(generation, token))
        {
        }
        catch (Exception)
        {
            _previewError = Text[WardrobeUi.PreviewUnavailable];
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Rejects completion callbacks belonging to an old page, appearance or connection.
    /// </summary>
    /// <param name="generation">The captured image job identity.</param>
    /// <param name="token">The captured image cancellation token.</param>
    /// <returns>Whether this job may update the visible dialog.</returns>
    private bool ImagesCurrent(int generation, CancellationToken token)
        => !_disposed && Connected && generation == _imageGeneration && !token.IsCancellationRequested;

    /// <summary>
    /// Reuses exact appearance-and-view previews while bounding retained native images.
    /// </summary>
    /// <param name="appearance">The image's appearance identity.</param>
    /// <param name="mode">The native viewing mode.</param>
    /// <param name="pieceKey">The candidate key for a catalog thumbnail, or null.</param>
    /// <param name="profileId">The saved profile for its thumbnail, or null.</param>
    /// <param name="headOnly">Whether the native portrait is cropped to its upper half.</param>
    /// <param name="dye">The native field whose swatches are requested.</param>
    /// <param name="token">The token cancelling obsolete work.</param>
    /// <returns>The validated native PNG and optional palette.</returns>
    private async Task<WardrobePreviewPayload> NativePreviewAsync(WardrobeAppearancePayload appearance, string mode, string? pieceKey, string? profileId, bool headOnly, string dye, CancellationToken token)
    {
        var key = (appearance, mode, pieceKey, profileId, _slot, headOnly, dye);
        if (_previewCache.TryGetValue(key, out WardrobePreviewPayload? cached))
            return cached;

        WardrobePreviewPayload result = await Client.PreviewAsync(mode, pieceKey, _slot, headOnly, dye, profileId, token);
        token.ThrowIfCancellationRequested();
        if (_previewCache.Count >= 96)
            _previewCache.Remove(_previewCache.Keys.First());

        _previewCache[key] = result;
        return result;
    }
}
