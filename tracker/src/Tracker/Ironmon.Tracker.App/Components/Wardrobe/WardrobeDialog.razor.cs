using Ironmon.Tracker.Connection.Wardrobe;
using Ironmon.Tracker.Protocol.Wardrobe;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Wardrobe;

/// <summary>
/// Edits a game-issued wardrobe in the approved compact Obsidian dialog.
/// </summary>
public partial class WardrobeDialog : IAsyncDisposable
{
    /// <summary>
    /// Defines the explicit saved-outfit form operations.
    /// </summary>
    private enum ProfileForm
    {
        /// <summary>
        /// Displays no saved-outfit form.
        /// </summary>
        None = 0,
        /// <summary>
        /// Creates a named saved outfit.
        /// </summary>
        New = 1,
        /// <summary>
        /// Renames a saved outfit.
        /// </summary>
        Rename = 2,
        /// <summary>
        /// Explicitly replaces a saved outfit.
        /// </summary>
        Replace = 3,
        /// <summary>
        /// Confirms saved-outfit removal.
        /// </summary>
        Delete = 4
    }

    /// <summary>
    /// Defines the small explicit purchase and discard confirmations.
    /// </summary>
    private enum Confirmation
    {
        /// <summary>
        /// Displays the ordinary editor.
        /// </summary>
        None = 0,
        /// <summary>
        /// Confirms one permanent purchase.
        /// </summary>
        Purchase = 1,
        /// <summary>
        /// Confirms appearance changes being discarded.
        /// </summary>
        Discard = 2
    }

    private const string _swatchPrefix = "--ow-swatch:";
    private const string _editedSuffix = " *";
    private const string _selectedItemClass = "ow-item selected";
    private const string _itemClass = "ow-item";
    private const string _imagePlaceholder = "ow-image-placeholder";
    private readonly Dictionary<string, string> _thumbnails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _profileImages = new(StringComparer.Ordinal);
    private readonly Dictionary<(WardrobeAppearancePayload Appearance, string Mode, string? Piece, string? Profile, string Slot, bool Head, string Dye), WardrobePreviewPayload> _previewCache = [];
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _images;
    private WardrobeSnapshotPayload _state = new();
    private HashSet<string> _owned = new(StringComparer.Ordinal);
    private WardrobeAppearancePayload? _imageAppearance;
    private string? _observedWardrobe;
    private string? _observedLoadedProfile;
    private string? _chosenProfile;
    private string? _mainImage;
    private string? _error;
    private string? _previewError;
    private string? _formError;
    private string? _formProfileId;
    private string _formName = string.Empty;
    private string _category = WardrobeProtocol.Clothes;
    private string _slot = WardrobeProtocol.Hat;
    private string _view = WardrobeProtocol.Portrait;
    private string _filter = WardrobeUi.AllFilter;
    private string _search = string.Empty;
    private string _colorTarget = WardrobeProtocol.HairColor;
    private IReadOnlyDictionary<int, string> _baseColors = new Dictionary<int, string>();
    private IReadOnlyDictionary<int, string> _skinColors = new Dictionary<int, string>();
    private IReadOnlyList<string> _dyeColors = [];
    private int _page;
    private int _imageGeneration;
    private bool _refreshImages;
    private bool _busy;
    private bool _maximized;
    private bool _disposed;
    private bool _wasConnected;
    private ProfileForm _form;
    private Confirmation _confirmation;

    /// <summary>
    /// Gets or sets the last acknowledged game-owned session.
    /// </summary>
    [Parameter, EditorRequired]
    public WardrobeSnapshotPayload Snapshot { get; set; } = new();

    /// <summary>
    /// Gets or sets whether the originating game currently accepts requests.
    /// </summary>
    [Parameter]
    public bool Connected { get; set; }

    /// <summary>
    /// Gets the shared game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Gets native tracker-window expansion and restoration.
    /// </summary>
    [Inject]
    private TrackerWindowService WindowService { get; set; } = null!;

    /// <summary>
    /// Gets the game-only wardrobe controller.
    /// </summary>
    private TrackerWardrobeClient Client => Requests.Wardrobe;

    /// <summary>
    /// Gets whether mutation controls must be unavailable.
    /// </summary>
    private bool Disabled => _busy || !Connected;

    /// <summary>
    /// Gets the native draft currently shown by the dialog.
    /// </summary>
    private WardrobeAppearancePayload Appearance => _state.Appearance;

    /// <summary>
    /// Gets the classes for the compact or expanded dialog.
    /// </summary>
    private string DialogClass => _maximized ? WardrobeUi.ExpandedDialogClass : WardrobeUi.NormalDialogClass;

    /// <summary>
    /// Gets the display identity of the appearance's save slot.
    /// </summary>
    private string SlotName => _state.AppearanceKey.StartsWith(WardrobeUi.SlotPrefix, StringComparison.Ordinal) ? _state.AppearanceKey[WardrobeUi.SlotPrefix.Length..] : _state.AppearanceKey;

    /// <summary>
    /// Gets the native hair's base-color variant index.
    /// </summary>
    private int HairVersion => int.TryParse(Appearance.Hair.Split('_', 2)[0], out int value) ? value : 0;

    /// <summary>
    /// Gets the native hairstyle identifier without its color variant.
    /// </summary>
    private string HairId => Appearance.Hair.Contains('_') ? Appearance.Hair[(Appearance.Hair.IndexOf('_') + 1)..] : string.Empty;

    /// <summary>
    /// Gets the selected category's native item key, or null for an empty accessory.
    /// </summary>
    private string? SelectedKey => _category switch
    {
        WardrobeProtocol.Clothes => WardrobeProtocol.Clothes + ':' + Appearance.Clothes,
        WardrobeProtocol.Hair => WardrobeProtocol.Hair + ':' + HairId,
        WardrobeProtocol.Hat => AccessoryKey(_slot == WardrobeProtocol.Hat ? Appearance.Hat : Appearance.Hat2),
        _ => null
    };

    /// <summary>
    /// Gets the selected installed item, when this tab contains items.
    /// </summary>
    private WardrobeCatalogItemPayload? SelectedItem => Client.Catalog.FirstOrDefault(item => item.Key == SelectedKey);

    /// <summary>
    /// Gets whether the selected item needs a permanent purchase.
    /// </summary>
    private bool Purchasable => SelectedItem is { } item && !_owned.Contains(item.Key) && !_state.InitialSelection;

    /// <summary>
    /// Gets whether the assembled look contains any unowned piece.
    /// </summary>
    private bool HasLockedPieces => AppearanceKeys(Appearance).Any(key => !_owned.Contains(key));

    /// <summary>
    /// Gets the label key for the active category's search field.
    /// </summary>
    private string SearchLabel => _category == WardrobeProtocol.Clothes ? WardrobeUi.SearchOutfits : _category == WardrobeProtocol.Hair ? WardrobeUi.SearchHair : WardrobeUi.SearchExtras;

    /// <summary>
    /// Gets the native hue field whose swatches are currently displayed.
    /// </summary>
    private string PaletteField => _category == WardrobeUi.ColorsCategory ? _colorTarget : WardrobeProtocol.HairColor;

    /// <summary>
    /// Gets the category's filtered installed items, retaining missing owned items in the ownership view.
    /// </summary>
    private IReadOnlyList<WardrobeCatalogItemPayload> FilteredItems => [.. Client.Catalog.Where(item => item.Category == _category && (item.Available || _filter == WardrobeUi.OwnedFilter && _owned.Contains(item.Key)) && (_filter == WardrobeUi.AllFilter || (_filter == WardrobeUi.OwnedFilter ? _owned.Contains(item.Key) : !_owned.Contains(item.Key))) && (item.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase) || item.Tags.Contains(_search, StringComparison.CurrentCultureIgnoreCase))).OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(item => item.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Gets the number of thumbnails shown on one compact page.
    /// </summary>
    private int PageSize => _category == WardrobeProtocol.Hair ? 3 : 6;

    /// <summary>
    /// Gets whether the fixed footer should display catalogue or saved-outfit navigation.
    /// </summary>
    private bool Browsing => _category != WardrobeUi.ColorsCategory && _confirmation == Confirmation.None && _form == ProfileForm.None;

    /// <summary>
    /// Gets the filtered item or saved-outfit count for the fixed footer.
    /// </summary>
    private int BrowseCount => _category == WardrobeUi.ProfilesCategory ? _state.OutfitProfiles.Count : FilteredItems.Count;

    /// <summary>
    /// Gets the category's page count, including its empty state.
    /// </summary>
    private int PageCount => Math.Max(1, (BrowseCount + PageSize - 1) / PageSize);

    /// <summary>
    /// Gets only the saved outfits on the current profile page.
    /// </summary>
    private IEnumerable<WardrobeOutfitProfilePayload> VisibleProfiles => _state.OutfitProfiles.Skip(_page * PageSize).Take(PageSize);

    /// <summary>
    /// Gets only the thumbnails needed for the currently visible page.
    /// </summary>
    private IReadOnlyList<WardrobeCatalogItemPayload> VisibleItems => [.. FilteredItems.Skip(Math.Min(_page, PageCount - 1) * PageSize).Take(PageSize)];

    /// <summary>
    /// Observes only monotonically accepted parameters and schedules native previews after rendering.
    /// </summary>
    protected override void OnParametersSet()
    {
        bool newSession = _observedWardrobe != Snapshot.WardrobeId;
        if (newSession)
        {
            _observedWardrobe = Snapshot.WardrobeId;
            _previewCache.Clear();
            _thumbnails.Clear();
            _profileImages.Clear();
            _chosenProfile = Snapshot.LoadedProfileId;
            _category = WardrobeProtocol.Clothes;
            _page = 0;
        }

        if (newSession || Snapshot.Revision >= _state.Revision)
            _state = Snapshot;

        _owned = new(_state.OwnedKeys, StringComparer.Ordinal);
        _page = Math.Clamp(_page, 0, PageCount - 1);
        if (_observedLoadedProfile != _state.LoadedProfileId)
        {
            _observedLoadedProfile = _state.LoadedProfileId;
            _chosenProfile = _state.LoadedProfileId;
        }

        if (!_busy && Connected && (newSession || !_wasConnected || _imageAppearance != Appearance))
            _refreshImages = true;

        if (!Connected)
            CancelImages();

        _wasConnected = Connected;
    }

    /// <summary>
    /// Loads native images after the dialog's initial useful render.
    /// </summary>
    /// <param name="firstRender">Whether this is the first completed render.</param>
    /// <returns>Completion after the current preview job has finished or become obsolete.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_refreshImages && !_busy && Connected && !_disposed)
        {
            _refreshImages = false;
            await LoadImagesAsync();
        }
    }

    /// <summary>
    /// Creates a category-qualified accessory key, preserving the empty accessory state.
    /// </summary>
    /// <param name="id">The native accessory identifier, or null.</param>
    /// <returns>The shared accessory key, or null.</returns>
    private static string? AccessoryKey(string? id)
        => string.IsNullOrEmpty(id) ? null : WardrobeProtocol.Hat + ':' + id;

    /// <summary>
    /// Enumerates the distinct pieces selected in an appearance.
    /// </summary>
    /// <param name="appearance">The native appearance.</param>
    /// <returns>The distinct permanent-ownership keys.</returns>
    private static IEnumerable<string> AppearanceKeys(WardrobeAppearancePayload appearance)
    {
        string style = appearance.Hair.Contains('_') ? appearance.Hair[(appearance.Hair.IndexOf('_') + 1)..] : string.Empty;
        return new[] { WardrobeProtocol.Clothes + ':' + appearance.Clothes, WardrobeProtocol.Hair + ':' + style, AccessoryKey(appearance.Hat), AccessoryKey(appearance.Hat2) }.OfType<string>().Distinct(StringComparer.Ordinal);
    }

    /// <summary>
    /// Looks up a native item's authored display name.
    /// </summary>
    /// <param name="category">The native item category.</param>
    /// <param name="id">The native item identifier, or null.</param>
    /// <returns>The authored display name, identifier fallback, or localized empty-slot label.</returns>
    private string ItemName(string category, string? id)
        => string.IsNullOrEmpty(id) ? Text[WardrobeUi.None] : Client.Catalog.FirstOrDefault(item => item.Category == category && item.Id == id)?.Name ?? id;

    /// <summary>
    /// Gets a native hue from one supported dye field.
    /// </summary>
    /// <param name="appearance">The native appearance.</param>
    /// <param name="field">The native hue field.</param>
    /// <returns>The hue in degrees.</returns>
    private static int DyeValue(WardrobeAppearancePayload appearance, string field) => field switch
    {
        WardrobeProtocol.HairColor => appearance.HairColor,
        WardrobeProtocol.ClothesColor => appearance.ClothesColor,
        WardrobeProtocol.HatColor => appearance.HatColor,
        WardrobeProtocol.Hat2Color => appearance.Hat2Color,
        WardrobeProtocol.BikeColor => appearance.BikeColor,
        _ => throw new ArgumentException("Invalid wardrobe dye field.", nameof(field))
    };

    /// <summary>
    /// Gets the localized label key for one supported hue field.
    /// </summary>
    /// <param name="field">The native hue field.</param>
    /// <returns>The label resource key.</returns>
    private static string DyeLabel(string field) => field switch
    {
        WardrobeProtocol.HairColor => WardrobeUi.HairDye,
        WardrobeProtocol.ClothesColor => WardrobeUi.ClothesDye,
        WardrobeProtocol.HatColor => WardrobeUi.HatDye,
        WardrobeProtocol.Hat2Color => WardrobeUi.Hat2Dye,
        WardrobeProtocol.BikeColor => WardrobeUi.BikeDye,
        _ => throw new ArgumentException("Invalid wardrobe dye field.", nameof(field))
    };

    /// <summary>
    /// Creates a native color swatch declaration with a neutral loading fallback.
    /// </summary>
    /// <param name="colors">The native indexed swatches.</param>
    /// <param name="index">The native variant or skin-tone index.</param>
    /// <returns>The color declaration.</returns>
    private static string IndexedColor(IReadOnlyDictionary<int, string> colors, int index)
        => _swatchPrefix + (colors.TryGetValue(index, out string? value) ? value : WardrobeUi.NeutralColor);

    /// <summary>
    /// Cancels requests and restores native window sizing when the game closes this editor.
    /// </summary>
    /// <returns>Completion after native compact-window restoration.</returns>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        CancelImages();
        _lifetime.Cancel();
        _lifetime.Dispose();
        if (_maximized)
            await WindowService.RestoreAsync();

        GC.SuppressFinalize(this);
    }
}
