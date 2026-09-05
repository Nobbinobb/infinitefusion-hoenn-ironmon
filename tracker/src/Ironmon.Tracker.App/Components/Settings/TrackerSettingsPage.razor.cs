using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Settings;

/// <summary>
/// Presents and edits tracker-owned user settings.
/// </summary>
public partial class TrackerSettingsPage : IDisposable
{
    private const int FavoritePageSize = 8;
    private const string MegabyteUnit = "MB";
    private const string AvailableSpritesCompleteKey = "Settings.Sprites.AvailableComplete";
    private const string SpriteDownloadWithUnavailableKey = "Settings.Sprites.CompleteWithUnavailable";
    private const string SpriteSynchronizationCurrentKey = "Settings.Sprites.Current";
    private CancellationTokenSource? _spriteInstallCancellation;
    private IReadOnlyList<PokemonSearchMatch> _favorites = [];
    private CustomSpriteInstallPlan? _spriteInstallPlan;
    private CustomSpriteInstallProgress? _spriteInstallProgress;
    private string? _graphDepthError;
    private string? _nodesPerRowError;
    private string? _maximumBstError;
    private string? _spriteInstallStatus;
    private int _favoritePage;
    private bool _showSpriteInstallConfirmation;
    private bool _spriteInstallPreparing;
    private bool _spriteReviewIncludesUnavailable;
    private bool _disposed;
    private bool _spriteInstallRunning;
    private bool _spriteDialogOpen;
    private bool _favoritesOpen;
    private bool _graphSettingsSaved;
    private const string _favoriteSeparator = ", ";

    /// <summary>
    /// Gets or initializes the persisted favorite-Pokemon store.
    /// </summary>
    [Inject]
    private FavoritePokemonStore FavoriteStore { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the connected-game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-local evolution graph preferences.
    /// </summary>
    [Inject]
    private EvolutionGraphSettings EvolutionGraphSettings { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the custom sprite-sheet installer.
    /// </summary>
    [Inject]
    private CustomSpriteSheetInstaller SpriteInstaller { get; set; } = null!;

    /// <summary>
    /// Gets or initializes the current game connection state.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Gets or initializes whether starter selection is controlled automatically.
    /// </summary>
    [Parameter]
    public bool AutoSelectStarter { get; set; }

    /// <summary>
    /// Gets or initializes the callback raised when automatic starter selection changes.
    /// </summary>
    [Parameter]
    public EventCallback<bool> AutoSelectStarterChanged { get; set; }

    /// <summary>
    /// Gets or initializes the inclusive generated-BST ceiling for automatic starter selection.
    /// </summary>
    [Parameter]
    public int? MaximumStarterBaseStatTotal { get; set; }

    /// <summary>
    /// Gets or initializes the callback raised when the maximum starter BST changes.
    /// </summary>
    [Parameter]
    public EventCallback<int?> MaximumStarterBaseStatTotalChanged { get; set; }

    /// <summary>
    /// Gets or initializes the callback raised when the Favorite Clause list changes.
    /// </summary>
    [Parameter]
    public EventCallback<IReadOnlyList<string>> FavoriteSpeciesIdsChanged { get; set; }

    /// <summary>
    /// Gets or initializes the current setting synchronization status.
    /// </summary>
    [Parameter]
    public string? Status { get; set; }

    /// <summary>
    /// Loads the persisted favorites for display.
    /// </summary>
    protected override void OnInitialized() => _favorites = FavoriteStore.Favorites;

    /// <summary>
    /// Gets the favorites shown on the active local page.
    /// </summary>
    private IEnumerable<PokemonSearchMatch> PagedFavorites => _favorites.Skip(_favoritePage * FavoritePageSize).Take(FavoritePageSize);

    /// <summary>
    /// Gets the number of local favorite pages.
    /// </summary>
    private int FavoritePageCount => Math.Max(1, (int)Math.Ceiling(_favorites.Count / (double)FavoritePageSize));

    /// <summary>
    /// Applies a changed automatic starter-selection value.
    /// </summary>
    /// <param name="args">The checkbox change event.</param>
    /// <returns>A task representing callback dispatch.</returns>
    private Task HandleAutoSelectChanged(ChangeEventArgs args)
    {
        _graphSettingsSaved = false;
        return AutoSelectStarterChanged.InvokeAsync(args.Value is bool enabled && enabled);
    }

    /// <summary>
    /// Validates and persists the graph neighborhood expansion depth.
    /// </summary>
    /// <param name="args">The numeric input change event.</param>
    private void HandleExpansionDepthChanged(ChangeEventArgs args)
    {
        string text = args.Value?.ToString()?.Trim() ?? string.Empty;
        if (!int.TryParse(text, out int value) || value < EvolutionGraphSettings.MinimumExpansionDepth || value > EvolutionGraphSettings.MaximumExpansionDepth)
        {
            _graphDepthError = Text["Settings.EvolutionGraph.ExpansionDepthValidation", EvolutionGraphSettings.MinimumExpansionDepth, EvolutionGraphSettings.MaximumExpansionDepth];
            return;
        }

        _graphDepthError = null;
        EvolutionGraphSettings.ExpansionDepth = value;
        _graphSettingsSaved = true;
    }

    /// <summary>
    /// Validates and persists the maximum number of nodes in one physical graph row.
    /// </summary>
    /// <param name="args">The numeric input change event.</param>
    private void HandleNodesPerRowChanged(ChangeEventArgs args)
    {
        string text = args.Value?.ToString()?.Trim() ?? string.Empty;
        if (!int.TryParse(text, out int value) || value < EvolutionGraphSettings.MinimumNodesPerRow || value > EvolutionGraphSettings.MaximumNodesPerRow)
        {
            _nodesPerRowError = Text["Settings.EvolutionGraph.NodesPerRowValidation", EvolutionGraphSettings.MinimumNodesPerRow, EvolutionGraphSettings.MaximumNodesPerRow];
            return;
        }

        _nodesPerRowError = null;
        EvolutionGraphSettings.NodesPerRow = value;
        _graphSettingsSaved = true;
    }

    /// <summary>
    /// Validates and applies a changed maximum-starter-BST value.
    /// </summary>
    /// <param name="args">The numeric input change event.</param>
    /// <returns>A task representing callback dispatch.</returns>
    private Task HandleMaximumBstChanged(ChangeEventArgs args)
    {
        string text = args.Value?.ToString()?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            _maximumBstError = null;
            _graphSettingsSaved = false;
            return MaximumStarterBaseStatTotalChanged.InvokeAsync(null);
        }

        if (!int.TryParse(text, out int value) || value < StarterSelectionConstants.MinimumBaseStatTotal || value > StarterSelectionConstants.MaximumBaseStatTotal)
        {
            _maximumBstError = Text["Settings.Starter.MaximumBstValidation", StarterSelectionConstants.MinimumBaseStatTotal, StarterSelectionConstants.MaximumBaseStatTotal];
            return Task.CompletedTask;
        }

        _maximumBstError = null;
        _graphSettingsSaved = false;
        return MaximumStarterBaseStatTotalChanged.InvokeAsync(value);
    }

    /// <summary>
    /// Inspects the installed sprite library before asking for synchronization confirmation.
    /// </summary>
    /// <returns>A task representing local manifest inspection.</returns>
    private Task ReviewSpriteInstallAsync()
        => PrepareSpriteInstallAsync(false);

    /// <summary>
    /// Reviews a fresh plan that includes previously unavailable resources without clearing their saved status.
    /// </summary>
    /// <returns>A task representing local manifest inspection.</returns>
    private Task ReviewUnavailableSpritesAsync()
        => PrepareSpriteInstallAsync(true);

    /// <summary>
    /// Inspects missing sheets and optionally includes remembered HTTP 404 resources for an explicit retry.
    /// </summary>
    /// <param name="includeUnavailable">Whether previously unavailable resources should be retried.</param>
    /// <returns>A task representing local manifest inspection.</returns>
    private async Task PrepareSpriteInstallAsync(bool includeUnavailable)
    {
        if (_spriteInstallPreparing || _spriteInstallRunning)
            return;

        bool openWhenReady = !_spriteDialogOpen;
        _spriteReviewIncludesUnavailable = includeUnavailable;
        _spriteInstallPreparing = true;
        _spriteInstallStatus = null;
        _spriteInstallPlan = null;
        _showSpriteInstallConfirmation = false;
        try
        {
            if (ConnectionState.Snapshot.Status == TrackerConnectionStatus.Connected)
            {
                _spriteInstallStatus = Text["Settings.Sprites.CloseGame"];
                return;
            }

            string? gameRoot = ConnectionState.Snapshot.Game?.GameRoot;
            _spriteInstallPlan = await Task.Run(() => SpriteInstaller.CreatePlan(gameRoot, includeUnavailable));
            if (_spriteInstallPlan.PendingSheetCount == 0)
            {
                _spriteInstallStatus = _spriteInstallPlan.UnavailableSheetCount == 0
                    ? Text["Settings.Sprites.CompleteAlready", _spriteInstallPlan.TotalSheetCount]
                    : Text[AvailableSpritesCompleteKey, _spriteInstallPlan.ExistingSheetCount, _spriteInstallPlan.UnavailableSheetCount];
                return;
            }

            _showSpriteInstallConfirmation = true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _spriteInstallStatus = Text["Settings.Sprites.InstallationNotFound"];
        }
        finally
        {
            _spriteInstallPreparing = false;
            if (openWhenReady && !_disposed)
                _spriteDialogOpen = true;
        }
    }

    /// <summary>
    /// Starts the confirmed resumable custom sprite-sheet synchronization.
    /// </summary>
    /// <returns>A task representing the download.</returns>
    private async Task StartSpriteInstallAsync()
    {
        if (_spriteInstallPlan is null || _spriteInstallRunning)
            return;

        _showSpriteInstallConfirmation = false;
        _spriteInstallStatus = null;
        _spriteInstallProgress = new CustomSpriteInstallProgress(0, _spriteInstallPlan.PendingSheetCount, 0, 0, _spriteInstallPlan.UnavailableSheetCount);
        _spriteInstallCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _spriteInstallCancellation.Token;
        _spriteInstallRunning = true;
        Progress<CustomSpriteInstallProgress> progress = new(value =>
        {
            _spriteInstallProgress = value;
            _ = InvokeAsync(StateHasChanged);
        });
        try
        {
            CustomSpriteInstallResult result = await SpriteInstaller.InstallAsync(_spriteInstallPlan, progress, cancellationToken);
            _spriteInstallStatus = result switch
            {
                { FailedSheetCount: > 0 } => Text["Settings.Sprites.Partial", result.DownloadedSheetCount, result.UnchangedSheetCount, result.FailedSheetCount, result.UnavailableSheetCount],
                { UnavailableSheetCount: > 0 } => Text[SpriteDownloadWithUnavailableKey, result.DownloadedSheetCount, result.UnchangedSheetCount, FormatBytes(result.DownloadedBytes), result.UnavailableSheetCount],
                { DownloadedSheetCount: 0 } => Text[SpriteSynchronizationCurrentKey, result.UnchangedSheetCount],
                _ => Text["Settings.Sprites.Complete", result.DownloadedSheetCount, result.UnchangedSheetCount, FormatBytes(result.DownloadedBytes)]
            };

            _spriteInstallPlan = null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _spriteInstallStatus = Text["Settings.Sprites.Cancelled"];
        }
        catch (InvalidOperationException)
        {
            _spriteInstallStatus = Text["Settings.Sprites.CloseGame"];
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            _spriteInstallStatus = Text["Settings.Sprites.DownloadError"];
        }
        finally
        {
            _spriteInstallRunning = false;
            _spriteInstallCancellation.Dispose();
            _spriteInstallCancellation = null;
        }
    }

    /// <summary>
    /// Hides the prepared download confirmation.
    /// </summary>
    private void CancelSpriteReview()
    {
        _spriteDialogOpen = false;
        _showSpriteInstallConfirmation = false;
        _spriteInstallPlan = null;
    }

    /// <summary>
    /// Cancels the active sprite-sheet download.
    /// </summary>
    private void CancelSpriteInstall()
        => _spriteInstallCancellation?.Cancel();

    /// <summary>
    /// Formats downloaded bytes for the compact progress display.
    /// </summary>
    /// <param name="bytes">The downloaded byte count.</param>
    /// <returns>The localized-scale megabyte display.</returns>
    private static string FormatBytes(long bytes)
        => $"{bytes / (1024d * 1024d):N1} {MegabyteUnit}";

    /// <summary>
    /// Gets a compact preview of the saved favorite species.
    /// </summary>
    private string FavoriteSummary
    {
        get
        {
            if (_favorites.Count == 0)
                return Text["Settings.Favorites.Empty"];

            string names = string.Join(_favoriteSeparator, _favorites.Take(3).Select(favorite => favorite.SpeciesName));
            return _favorites.Count > 3 ? names + Text["Redesign.Settings.MoreFavorites", _favorites.Count - 3] : names;
        }
    }

    /// <summary>
    /// Opens the dialog containing favorite management and suggestions.
    /// </summary>
    private void OpenFavorites() => _favoritesOpen = true;

    /// <summary>
    /// Closes favorite management and cancels the search component's outstanding work.
    /// </summary>
    private void CloseFavorites() => _favoritesOpen = false;

    /// <summary>
    /// Shows an active synchronization or prepares a fresh library review.
    /// </summary>
    /// <returns>A task representing optional library inspection.</returns>
    private Task OpenSpriteReviewAsync()
    {
        if (_spriteInstallRunning)
        {
            _spriteDialogOpen = true;
            return Task.CompletedTask;
        }

        return _spriteInstallPreparing ? Task.CompletedTask : ReviewSpriteInstallAsync();
    }

    /// <summary>
    /// Dismisses the synchronization dialog without cancelling an active transfer.
    /// </summary>
    private void CloseSpriteDialog() => _spriteDialogOpen = false;

    /// <summary>
    /// Searches the connected game's normal species for up to ten suggestions.
    /// </summary>
    /// <param name="query">The partial Pokémon name.</param>
    /// <param name="cancellationToken">The cancellation token for superseded input.</param>
    /// <returns>The first matching normal Pokémon.</returns>
    private async Task<IReadOnlyList<PokemonSearchMatch>> SearchFavoritesAsync(string query, CancellationToken cancellationToken)
    {
        PokemonSearchResponsePayload response = await Requests.SearchFavoritePokemonAsync(query, limit: 10, cancellationToken: cancellationToken);
        return response.Matches;
    }

    /// <summary>
    /// Distinguishes a failed search from a missing game connection.
    /// </summary>
    /// <returns>The localized message for the current connection state.</returns>
    private string GetFavoriteSearchErrorMessage()
    {
        return Text[ConnectionState.Snapshot.Status == TrackerConnectionStatus.Connected
            ? "Settings.Favorites.SearchFailed"
            : "Settings.Favorites.ConnectToSearch"];
    }

    /// <summary>
    /// Adds one search suggestion to the persisted Favorite Clause list.
    /// </summary>
    /// <param name="match">The selected normal Pokemon.</param>
    /// <returns>A task representing settings synchronization.</returns>
    private async Task AddFavoriteAsync(PokemonSearchMatch match)
    {
        FavoriteStore.Add(match);
        _favorites = FavoriteStore.Favorites;
        await PublishFavoritesAsync();
    }

    /// <summary>
    /// Removes one Pokemon from the persisted Favorite Clause list.
    /// </summary>
    /// <param name="speciesId">The stable normal-species identifier.</param>
    /// <returns>A task representing settings synchronization.</returns>
    private async Task RemoveFavoriteAsync(string speciesId)
    {
        FavoriteStore.Remove(speciesId);
        _favorites = FavoriteStore.Favorites;
        _favoritePage = Math.Min(_favoritePage, FavoritePageCount - 1);
        await PublishFavoritesAsync();
    }

    /// <summary>
    /// Gets whether a stable species is already in the Favorite Clause list.
    /// </summary>
    /// <param name="speciesId">The stable normal-species identifier.</param>
    /// <returns><see langword="true"/> when already present.</returns>
    private bool IsFavorite(string speciesId)
        => _favorites.Any(favorite => favorite.SpeciesId.Equals(speciesId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Publishes the complete current Favorite Clause list.
    /// </summary>
    /// <returns>A task representing callback dispatch.</returns>
    private Task PublishFavoritesAsync()
    {
        _graphSettingsSaved = false;
        return FavoriteSpeciesIdsChanged.InvokeAsync([.. _favorites.Select(favorite => favorite.SpeciesId)]);
    }

    /// <summary>
    /// Moves to the previous favorite page.
    /// </summary>
    private void PreviousFavoritePage() => _favoritePage = Math.Max(0, _favoritePage - 1);

    /// <summary>
    /// Moves to the next favorite page.
    /// </summary>
    private void NextFavoritePage() => _favoritePage = Math.Min(FavoritePageCount - 1, _favoritePage + 1);

    /// <summary>
    /// Cancels an active sprite synchronization when the settings view closes.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        _spriteInstallCancellation?.Cancel();
        _spriteInstallCancellation?.Dispose();
    }
}
