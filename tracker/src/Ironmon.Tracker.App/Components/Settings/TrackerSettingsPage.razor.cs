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
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _spriteInstallCancellation;
    private IReadOnlyList<PokemonSearchMatch> _favorites = [];
    private IReadOnlyList<PokemonSearchMatch> _matches = [];
    private CustomSpriteInstallPlan? _spriteInstallPlan;
    private CustomSpriteInstallProgress? _spriteInstallProgress;
    private string _query = string.Empty;
    private string? _graphDepthError;
    private string? _nodesPerRowError;
    private string? _searchError;
    private string? _maximumBstError;
    private string? _spriteInstallStatus;
    private int _favoritePage;
    private bool _searching;
    private bool _showSpriteInstallConfirmation;
    private bool _spriteInstallPreparing;
    private bool _spriteInstallRunning;

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
        => AutoSelectStarterChanged.InvokeAsync(args.Value is bool enabled && enabled);

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
            return MaximumStarterBaseStatTotalChanged.InvokeAsync(null);
        }

        if (!int.TryParse(text, out int value) || value < StarterSelectionConstants.MinimumBaseStatTotal || value > StarterSelectionConstants.MaximumBaseStatTotal)
        {
            _maximumBstError = Text["Settings.Starter.MaximumBstValidation", StarterSelectionConstants.MinimumBaseStatTotal, StarterSelectionConstants.MaximumBaseStatTotal];
            return Task.CompletedTask;
        }

        _maximumBstError = null;
        return MaximumStarterBaseStatTotalChanged.InvokeAsync(value);
    }

    /// <summary>
    /// Inspects the installed sprite library before asking for download confirmation.
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
        }
    }

    /// <summary>
    /// Starts the confirmed resumable custom sprite-sheet installation.
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
                { FailedSheetCount: > 0 } => Text["Settings.Sprites.Partial", result.DownloadedSheetCount, result.FailedSheetCount, result.UnavailableSheetCount],
                { UnavailableSheetCount: > 0 } => Text[SpriteDownloadWithUnavailableKey, result.DownloadedSheetCount, FormatBytes(result.DownloadedBytes), result.UnavailableSheetCount],
                _ => Text["Settings.Sprites.Complete", result.DownloadedSheetCount, FormatBytes(result.DownloadedBytes)]
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
    /// Debounces user input and requests normal-Pokemon suggestions.
    /// </summary>
    /// <param name="args">The search input event.</param>
    /// <returns>A task representing the suggestion request.</returns>
    private async Task HandleSearchInput(ChangeEventArgs args)
    {
        _query = args.Value?.ToString()?.Trim() ?? string.Empty;
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _searchCancellation.Token;
        _matches = [];
        _searchError = null;
        _searching = false;
        if (_query.Length < 2)
            return;

        try
        {
            _searching = true;
            await Task.Delay(250, cancellationToken);
            PokemonSearchResponsePayload response = await Requests.SearchFavoritePokemonAsync(_query, limit: 8, cancellationToken: cancellationToken);
            _matches = response.Matches;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException)
        {
            _searchError = Text["Settings.Favorites.ConnectToSearch"];
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _searching = false;
        }
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
        => FavoriteSpeciesIdsChanged.InvokeAsync(_favorites.Select(favorite => favorite.SpeciesId).ToArray());

    /// <summary>
    /// Moves to the previous favorite page.
    /// </summary>
    private void PreviousFavoritePage() => _favoritePage = Math.Max(0, _favoritePage - 1);

    /// <summary>
    /// Moves to the next favorite page.
    /// </summary>
    private void NextFavoritePage() => _favoritePage = Math.Min(FavoritePageCount - 1, _favoritePage + 1);

    /// <summary>
    /// Cancels pending suggestion work when the settings view closes.
    /// </summary>
    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _spriteInstallCancellation?.Cancel();
        _spriteInstallCancellation?.Dispose();
    }
}
