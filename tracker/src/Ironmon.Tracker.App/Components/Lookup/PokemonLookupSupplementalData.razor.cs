using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders authored occurrences and Pokemon relationships shared by all complete information entry points.
/// </summary>
public partial class PokemonLookupSupplementalData
{
    private PokemonLookupOverviewSnapshot? _observedOverview;
    private FusionMaterialSearchResponsePayload _fusionMaterials = new();
    private TrainerOccurrenceSearchResponsePayload _trainerOccurrences = new();
    private WildOccurrenceSearchResponsePayload _wildOccurrences = new();
    private int _fusionMaterialOffset;
    private int _trainerOccurrenceOffset;
    private int _wildOccurrenceOffset;
    private bool _loadingFusionMaterials;
    private bool _loadingTrainerOccurrences;
    private bool _loadingWildOccurrences;
    private string? _fusionMaterialError;
    private string? _trainerOccurrenceError;
    private string? _wildOccurrenceError;

    /// <summary>
    /// Gets or sets the request client used to load another material page.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets the required overview section.
    /// </summary>
    private PokemonLookupOverviewSnapshot Overview
        => Pokemon.Overview ?? throw new InvalidOperationException("The Overview lookup response is missing its section payload.");

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
    protected override void OnParametersSet()
    {
        if (ReferenceEquals(_observedOverview, Overview))
            return;

        _observedOverview = Overview;
        _fusionMaterials = Overview.FusionMaterials;
        _trainerOccurrences = Overview.TrainerOccurrences;
        _wildOccurrences = Overview.WildOccurrences;
        _fusionMaterialOffset = 0;
        _trainerOccurrenceOffset = 0;
        _wildOccurrenceOffset = 0;
        _fusionMaterialError = null;
        _trainerOccurrenceError = null;
        _wildOccurrenceError = null;
    }

    /// <summary>
    /// Loads one bounded wild-occurrence page through the selected lookup channel.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadWildOccurrencePageAsync(int offset)
    {
        if (_loadingWildOccurrences)
            return;

        _loadingWildOccurrences = true;
        _wildOccurrenceError = null;
        try
        {
            WildOccurrenceSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugWildOccurrencesAsync(Pokemon.Identity.SpeciesId, offset)
                : await Connection.SearchWildOccurrencesAsync(Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]), Pokemon.Identity.SpeciesId, offset);
            _wildOccurrences = response;
            _wildOccurrenceOffset = offset;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _wildOccurrenceError = exception.Message;
        }
        finally
        {
            _loadingWildOccurrences = false;
        }
    }

    /// <summary>
    /// Loads one bounded trainer-occurrence page through the selected lookup channel.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadTrainerOccurrencePageAsync(int offset)
    {
        if (_loadingTrainerOccurrences)
            return;

        _loadingTrainerOccurrences = true;
        _trainerOccurrenceError = null;
        try
        {
            TrainerOccurrenceSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugTrainerOccurrencesAsync(Pokemon.Identity.SpeciesId, offset)
                : await Connection.SearchTrainerOccurrencesAsync(Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]), Pokemon.Identity.SpeciesId, offset);
            _trainerOccurrences = response;
            _trainerOccurrenceOffset = offset;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _trainerOccurrenceError = exception.Message;
        }
        finally
        {
            _loadingTrainerOccurrences = false;
        }
    }

    /// <summary>
    /// Loads the preceding wild-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task PreviousWildOccurrencePageAsync()
        => LoadWildOccurrencePageAsync(Math.Max(0, _wildOccurrenceOffset - TrackerProtocol.OccurrencePageSize));

    /// <summary>
    /// Loads the following wild-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task NextWildOccurrencePageAsync()
        => LoadWildOccurrencePageAsync(_wildOccurrenceOffset + TrackerProtocol.OccurrencePageSize);

    /// <summary>
    /// Loads the preceding trainer-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task PreviousTrainerOccurrencePageAsync()
        => LoadTrainerOccurrencePageAsync(Math.Max(0, _trainerOccurrenceOffset - TrackerProtocol.OccurrencePageSize));

    /// <summary>
    /// Loads the following trainer-occurrence page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task NextTrainerOccurrencePageAsync()
        => LoadTrainerOccurrencePageAsync(_trainerOccurrenceOffset + TrackerProtocol.OccurrencePageSize);

    /// <summary>
    /// Formats the visible occurrence result range.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <param name="count">The number of results on the current page.</param>
    /// <param name="total">The complete result count.</param>
    /// <returns>The inclusive result range and total.</returns>
    private string GetOccurrenceRangeText(int offset, int count, int total)
    {
        int first = count == 0 ? 0 : offset + 1;
        int last = offset + count;
        return Text["Lookup.Fusion.ResultRange", first, last, total];
    }

    /// <summary>
    /// Loads one bounded fusion-material page through the selected lookup channel.
    /// </summary>
    /// <param name="offset">The zero-based result offset.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadFusionMaterialPageAsync(int offset)
    {
        if (_loadingFusionMaterials)
            return;

        _loadingFusionMaterials = true;
        _fusionMaterialError = null;
        try
        {
            FusionMaterialSearchResponsePayload response = DebugMode
                ? await Connection.SearchDebugFusionMaterialsAsync(Pokemon.Identity.SpeciesId, offset)
                : await Connection.SearchFusionMaterialsAsync(Recipe ?? throw new InvalidOperationException(Text["Lookup.Fusion.CompletedRunRecipeRequired"]), Pokemon.Identity.SpeciesId, offset);
            _fusionMaterials = response;
            _fusionMaterialOffset = offset;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _fusionMaterialError = exception.Message;
        }
        finally
        {
            _loadingFusionMaterials = false;
        }
    }

    /// <summary>
    /// Loads the preceding fusion-material page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task PreviousFusionMaterialPageAsync()
        => LoadFusionMaterialPageAsync(Math.Max(0, _fusionMaterialOffset - TrackerProtocol.FusionMaterialPageSize));

    /// <summary>
    /// Loads the following fusion-material page.
    /// </summary>
    /// <returns>A task representing the request.</returns>
    private Task NextFusionMaterialPageAsync()
        => LoadFusionMaterialPageAsync(_fusionMaterialOffset + TrackerProtocol.FusionMaterialPageSize);

    /// <summary>
    /// Formats the visible fusion-material result range.
    /// </summary>
    /// <returns>The inclusive result range and total.</returns>
    private string GetFusionMaterialRangeText()
    {
        int first = _fusionMaterials.Matches.Count == 0 ? 0 : _fusionMaterialOffset + 1;
        int last = _fusionMaterialOffset + _fusionMaterials.Matches.Count;
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
    /// Formats one authored wild-encounter level or level range.
    /// </summary>
    /// <param name="minimumLevel">The minimum encounter level.</param>
    /// <param name="maximumLevel">The maximum encounter level.</param>
    /// <returns>The compact level label.</returns>
    private string FormatLevelRange(int minimumLevel, int maximumLevel)
        => minimumLevel == maximumLevel ? Text["Lookup.Card.LevelValue", minimumLevel] : Text["Lookup.Card.LevelRange", minimumLevel, maximumLevel];
}
