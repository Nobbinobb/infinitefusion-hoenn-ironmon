using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders authored occurrences and Pokemon relationships shared by all complete information entry points.
/// </summary>
public partial class PokemonLookupSupplementalData : IDisposable
{
    private const string _overworldOriginPrefix = "overworld";
    private readonly PaginationState _fusionMaterialPagination = new(TrackerProtocol.FusionMaterialPageSize);
    private readonly PaginationState _trainerOccurrencePagination = new(TrackerProtocol.OccurrencePageSize);
    private readonly PaginationState _wildOccurrencePagination = new(TrackerProtocol.OccurrencePageSize);
    private string? _observedPokemonKey;
    private bool? _observedOverworldMode;
    private bool _disposed;
    private FusionMaterialSearchResponsePayload _fusionMaterials = new();
    private TrainerOccurrenceSearchResponsePayload _trainerOccurrences = new();
    private WildOccurrenceSearchResponsePayload _wildOccurrences = new();
    private bool _loadingFusionMaterials;
    private bool _loadingTrainerOccurrences;
    private bool _loadingWildOccurrences;
    private bool _loadFusionMaterialsAfterRender;
    private CancellationTokenSource? _fusionMaterialCancellation;
    private CancellationTokenSource? _occurrenceCancellation;
    private string? _fusionMaterialError;
    private string? _trainerOccurrenceError;
    private string? _wildOccurrenceError;

    /// <summary>
    /// Gets or sets the request client used to load another material page.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the live encounter-option notifications used by diagnostic locations.
    /// </summary>
    [Inject]
    private TrackerConnectionState ConnectionState { get; set; } = null!;

    /// <summary>
    /// Subscribes to encounter-option changes while this overview remains open.
    /// </summary>
    protected override void OnInitialized()
    {
        ConnectionState.Changed += HandleConnectionChanged;
    }

    /// <summary>
    /// Reloads diagnostic locations when the active encounter option changes.
    /// </summary>
    /// <param name="sender">The connection state publishing the change.</param>
    /// <param name="args">The change notification.</param>
    private void HandleConnectionChanged(object? sender, EventArgs args)
    {
        if (_disposed || !DebugMode || ConnectionState.Snapshot.CurrentState?.OverworldEncounters == _observedOverworldMode)
            return;

        _ = InvokeAsync(async () =>
        {
            if (_disposed)
                return;

            await OnParametersSetAsync();
            if (!_disposed)
                StateHasChanged();
        });
    }

    /// <summary>
    /// Gets the required overview section.
    /// </summary>
    private PokemonLookupOverviewSnapshot Overview
        => Pokemon.Overview ?? new PokemonLookupOverviewSnapshot();

    /// <summary>
    /// Gets or sets the reconstructed Pokemon information.
    /// </summary>
    [Parameter]
    public PokemonLookupSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether related requests use the active debug run.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the optional live source that securely supplies the represented species.
    /// </summary>
    [Parameter]
    public DebugPokemonTarget? DebugTarget { get; set; }

    /// <summary>
    /// Gets or sets the enemy battler position when the live source is an enemy.
    /// </summary>
    [Parameter]
    public int? DebugEnemyPosition { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a related Pokemon is selected.
    /// </summary>
    [Parameter]
    public EventCallback<string> PokemonSelected { get; set; }

    /// <summary>
    /// Resets fusion-material paging when a new Overview response is displayed.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        _observedOverworldMode = DebugMode ? ConnectionState.Snapshot.CurrentState?.OverworldEncounters : null;
        string key = $"{Pokemon.Identity.SpeciesId}|{Pokemon.Overview?.GetHashCode()}|{DebugMode}|{DebugTarget}|{DebugEnemyPosition}|{_observedOverworldMode}";
        if (_observedPokemonKey == key)
            return;

        _fusionMaterialCancellation?.Cancel();
        _fusionMaterialCancellation?.Dispose();
        _fusionMaterialCancellation = null;
        _occurrenceCancellation?.Cancel();
        _occurrenceCancellation?.Dispose();
        _occurrenceCancellation = new CancellationTokenSource();
        _observedPokemonKey = key;
        _fusionMaterials = new FusionMaterialSearchResponsePayload();
        _trainerOccurrences = Overview.TrainerOccurrences;
        _wildOccurrences = DebugMode ? new WildOccurrenceSearchResponsePayload() : Overview.WildOccurrences;
        _fusionMaterialPagination.Reset();
        _trainerOccurrencePagination.Reset();
        _wildOccurrencePagination.Reset();
        _fusionMaterialError = null;
        _trainerOccurrenceError = null;
        _wildOccurrenceError = null;
        _loadingFusionMaterials = false;
        _loadingWildOccurrences = false;
        _loadingTrainerOccurrences = false;
        _loadFusionMaterialsAfterRender = Pokemon.Identity.Fusion
            && (!DebugMode || Connection.HasDiagnosticCapability(DiagnosticCapabilities.FusionMaterialPairs));

        if (_loadFusionMaterialsAfterRender)
            _fusionMaterialCancellation = new CancellationTokenSource();

        List<Task> loads = [];
        if (DebugMode ? Connection.HasDiagnosticCapability(DiagnosticCapabilities.WorldWildEncounters) : Overview.WildOccurrences.Pending)
            loads.Add(LoadWildOccurrencePageAsync(0));

        if ((Overview.TrainerOccurrences.Pending || Pokemon.Overview is null) && (!DebugMode || Connection.HasDiagnosticCapability(DiagnosticCapabilities.WorldTrainerParties)))
            loads.Add(LoadTrainerOccurrencePageAsync(0));

        await Task.WhenAll(loads);
    }

    /// <summary>
    /// Starts fusion-material reconstruction only after the Overview has rendered.
    /// </summary>
    /// <param name="firstRender">Whether this is the component's first render.</param>
    /// <returns>A task representing the progressive material request.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_loadFusionMaterialsAfterRender)
            return;

        _loadFusionMaterialsAfterRender = false;
        await LoadFusionMaterialPageAsync(0);
    }

    /// <summary>
    /// Loads one bounded wild-occurrence page through the selected lookup channel.
    /// </summary>
    /// <param name="pageIndex">The zero-based result page.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadWildOccurrencePageAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (_loadingWildOccurrences)
            return;

        CancellationToken cancellationToken = _occurrenceCancellation?.Token ?? CancellationToken.None;
        _loadingWildOccurrences = true;
        _wildOccurrenceError = null;
        try
        {
            int offset = checked(pageIndex * _wildOccurrencePagination.PageSize);
            WildOccurrenceSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugWildOccurrencesAsync(Pokemon.Identity.SpeciesId, offset, DebugTarget, DebugEnemyPosition, cancellationToken)
                : await Connection.SearchWildOccurrencesAsync(Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]), Pokemon.Identity.SpeciesId, offset, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _wildOccurrences = response;
            _wildOccurrencePagination.Select(pageIndex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            if (!cancellationToken.IsCancellationRequested)
                _wildOccurrenceError = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _loadingWildOccurrences = false;
        }
    }

    /// <summary>
    /// Loads one bounded trainer-occurrence page through the selected lookup channel.
    /// </summary>
    /// <param name="pageIndex">The zero-based result page.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadTrainerOccurrencePageAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (_loadingTrainerOccurrences)
            return;

        CancellationToken cancellationToken = _occurrenceCancellation?.Token ?? CancellationToken.None;
        _loadingTrainerOccurrences = true;
        _trainerOccurrenceError = null;
        try
        {
            int offset = checked(pageIndex * _trainerOccurrencePagination.PageSize);
            TrainerOccurrenceSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugTrainerOccurrencesAsync(Pokemon.Identity.SpeciesId, offset, DebugTarget, DebugEnemyPosition, cancellationToken)
                : await Connection.SearchTrainerOccurrencesAsync(Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]), Pokemon.Identity.SpeciesId, offset, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _trainerOccurrences = response;
            _trainerOccurrencePagination.Select(pageIndex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            if (!cancellationToken.IsCancellationRequested)
                _trainerOccurrenceError = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _loadingTrainerOccurrences = false;
        }
    }

    /// <summary>
    /// Loads the preceding wild-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task PreviousWildOccurrencePageAsync()
        => LoadWildOccurrencePageAsync(_wildOccurrencePagination.PageIndex - 1);

    /// <summary>
    /// Loads the following wild-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task NextWildOccurrencePageAsync()
        => LoadWildOccurrencePageAsync(_wildOccurrencePagination.PageIndex + 1);

    /// <summary>
    /// Loads the preceding trainer-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task PreviousTrainerOccurrencePageAsync()
        => LoadTrainerOccurrencePageAsync(_trainerOccurrencePagination.PageIndex - 1);

    /// <summary>
    /// Loads the following trainer-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task NextTrainerOccurrencePageAsync()
        => LoadTrainerOccurrencePageAsync(_trainerOccurrencePagination.PageIndex + 1);

    /// <summary>
    /// Formats the visible occurrence result range.
    /// </summary>
    /// <param name="pagination">The selected result pagination.</param>
    /// <param name="count">The number of results on the current page.</param>
    /// <param name="total">The complete result count.</param>
    /// <returns>The inclusive result range and total.</returns>
    private string GetOccurrenceRangeText(PaginationState pagination, int count, int total)
    {
        (int first, int last) = pagination.GetRange(total, count);
        return Text["Lookup.Fusion.ResultRange", first, last, total];
    }

    /// <summary>
    /// Loads one bounded fusion-material page through the selected lookup channel.
    /// </summary>
    /// <param name="pageIndex">The zero-based result page.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadFusionMaterialPageAsync(int pageIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pageIndex);
        if (_loadingFusionMaterials)
            return;

        CancellationToken cancellationToken = _fusionMaterialCancellation?.Token ?? CancellationToken.None;
        _loadingFusionMaterials = true;
        _fusionMaterialError = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            int offset = checked(pageIndex * _fusionMaterialPagination.PageSize);
            FusionMaterialSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugFusionMaterialsAsync(Pokemon.Identity.SpeciesId, offset, DebugTarget, DebugEnemyPosition, cancellationToken)
                : await Connection.SearchFusionMaterialsAsync(Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]), Pokemon.Identity.SpeciesId, offset, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            _fusionMaterials = response;
            _fusionMaterialPagination.Select(pageIndex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _fusionMaterialError = exception.Message;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                _loadingFusionMaterials = false;
                await InvokeAsync(StateHasChanged);
            }
        }
    }

    /// <summary>
    /// Retries the currently selected fusion-material page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task RetryFusionMaterialPageAsync()
        => LoadFusionMaterialPageAsync(_fusionMaterialPagination.PageIndex);

    /// <summary>
    /// Loads the preceding fusion-material page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task PreviousFusionMaterialPageAsync()
        => LoadFusionMaterialPageAsync(_fusionMaterialPagination.PageIndex - 1);

    /// <summary>
    /// Loads the following fusion-material page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task NextFusionMaterialPageAsync()
        => LoadFusionMaterialPageAsync(_fusionMaterialPagination.PageIndex + 1);

    /// <summary>
    /// Formats the visible fusion-material result range.
    /// </summary>
    /// <returns>The inclusive result range and total.</returns>
    private string GetFusionMaterialRangeText()
    {
        (int first, int last) = _fusionMaterialPagination.GetRange(_fusionMaterials.Total, _fusionMaterials.Matches.Count);
        return Text["Lookup.Fusion.ResultRange", first, last, _fusionMaterials.Total];
    }

    /// <summary>
    /// Formats an authored encounter-table chance.
    /// </summary>
    /// <param name="chance">The percentage chance.</param>
    /// <param name="conditional">Whether the chance assumes a fusion event already triggered.</param>
    /// <returns>The compact percentage label.</returns>
    private string FormatChance(decimal chance, bool conditional)
        => conditional ? Text["Lookup.Card.ChanceWhenFused", chance] : $"{chance:0.##}%";

    /// <summary>
    /// Formats the authored slot or ordered slot pair for a wild occurrence.
    /// </summary>
    /// <param name="occurrence">The represented wild occurrence.</param>
    /// <returns>The compact slot label.</returns>
    private string FormatWildSlots(WildPokemonOccurrenceSnapshot occurrence)
        => occurrence.SecondarySlot is null ? Text["Lookup.Card.SlotNumber", occurrence.Slot] : Text["Lookup.Card.CombinedSlots", occurrence.Slot, occurrence.SecondarySlot];

    /// <summary>
    /// Formats both material tables when an encounter crosses environment, time, or weather tables.
    /// </summary>
    /// <param name="occurrence">The represented wild occurrence.</param>
    /// <returns>The source table or ordered table pair without internal version identifiers.</returns>
    private static string FormatWildTables(WildPokemonOccurrenceSnapshot occurrence)
    {
        return occurrence.CrossEnvironment
            ? $"{occurrence.EncounterType} + {occurrence.SecondaryEncounterType}"
            : occurrence.EncounterType;
    }

    /// <summary>
    /// Formats the encounter mechanic and its fusion roll without implying a material-pair probability.
    /// </summary>
    /// <param name="occurrence">The derived wild occurrence.</param>
    /// <returns>The localized mechanic and fusion-roll percentage.</returns>
    private string FormatWildFusionOrigin(WildPokemonOccurrenceSnapshot occurrence)
    {
        string mechanic = occurrence.Origin?.StartsWith(_overworldOriginPrefix, StringComparison.Ordinal) == true
            ? Text["Lookup.Areas.FusionOrigin.Overworld"]
            : Text["Lookup.Areas.FusionOrigin.Standard"];

        return Text["Lookup.Areas.FusionChance", mechanic, occurrence.FusionChancePercent.GetValueOrDefault()];
    }

    /// <summary>
    /// Formats one authored wild-encounter level or level range.
    /// </summary>
    /// <param name="minimumLevel">The minimum encounter level.</param>
    /// <param name="maximumLevel">The maximum encounter level.</param>
    /// <returns>The compact level label.</returns>
    private string FormatLevelRange(int minimumLevel, int maximumLevel)
        => minimumLevel == maximumLevel ? Text["Lookup.Card.LevelValue", minimumLevel] : Text["Lookup.Card.LevelRange", minimumLevel, maximumLevel];

    /// <summary>
    /// Cancels material reconstruction when the represented Pokemon leaves the page.
    /// </summary>
    public void Dispose()
    {
        _disposed = true;
        ConnectionState.Changed -= HandleConnectionChanged;
        _fusionMaterialCancellation?.Cancel();
        _fusionMaterialCancellation?.Dispose();
        _occurrenceCancellation?.Cancel();
        _occurrenceCancellation?.Dispose();
    }
}
