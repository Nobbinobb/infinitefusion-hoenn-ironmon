using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Presents lazily loaded area progress for an active or archived run.
/// </summary>
public partial class AreaLookupExplorer : IDisposable
{
    private static readonly AreaContentCategory[] Categories = [AreaContentCategory.Trainer, AreaContentCategory.Encounter, AreaContentCategory.Item];
    private readonly Dictionary<string, AreaLookupDetailResponsePayload> _details = [];
    private readonly Dictionary<string, string> _detailErrors = [];
    private readonly Dictionary<string, string?> _encounterSpriteSources = [];
    private readonly Dictionary<string, string?> _trainerSpriteSources = [];
    private readonly HashSet<string> _expandedAreas = [];
    private readonly HashSet<string> _loadingDetails = [];
    private IReadOnlyList<AreaSummaryPayload> _areas = [];
    private string? _enlargedSpriteLabel;
    private string? _enlargedSpriteSource;
    private AreaContentCategory _selectedCategory = AreaContentCategory.Trainer;
    private string? _observedSourceKey;
    private string? _error;
    private bool _loadingSummaries;

    /// <summary>
    /// Gets or initializes the active game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or initializes tracker-owned area discovery persistence.
    /// </summary>
    [Inject]
    private AreaDiscoveryStore Discoveries { get; set; } = null!;

    /// <summary>
    /// Gets or sets the archived recipe, or null for the active run.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets the stable key identifying the selected lookup source.
    /// </summary>
    [Parameter]
    public required string SourceKey { get; set; }

    /// <summary>
    /// Gets or sets the selected run identifier.
    /// </summary>
    [Parameter]
    public required string RunId { get; set; }

    /// <summary>
    /// Gets or sets the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Subscribes to tracker-owned discoveries received regardless of the visible page.
    /// </summary>
    protected override void OnInitialized()
        => Discoveries.Changed += HandleDiscoveryChanged;

    /// <summary>
    /// Reloads compact summaries when the selected run changes.
    /// </summary>
    /// <returns>A task representing summary loading.</returns>
    protected override async Task OnParametersSetAsync()
    {
        if (_observedSourceKey == SourceKey)
            return;

        _observedSourceKey = SourceKey;
        _areas = [];
        _details.Clear();
        _detailErrors.Clear();
        _encounterSpriteSources.Clear();
        _trainerSpriteSources.Clear();
        _expandedAreas.Clear();
        _loadingDetails.Clear();
        _loadingSummaries = true;
        _error = null;
        try
        {
            AreaLookupSummaryResponsePayload response = await Connection.GetAreaSummariesAsync(_selectedCategory, Recipe, forceRefresh: true);
            _areas = response.Areas;
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            _error = exception.Message;
        }
        finally
        {
            _loadingSummaries = false;
        }
    }

    /// <summary>
    /// Selects an area content category and closes groups from the previous category.
    /// </summary>
    /// <param name="category">The selected category.</param>
    private async Task SelectCategory(AreaContentCategory category)
    {
        _selectedCategory = category;
        _expandedAreas.Clear();
        await LoadSummariesAsync(true);
    }

    /// <summary>
    /// Expands or collapses an area, loading its selected category on first expansion.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>A task representing optional detail loading.</returns>
    private async Task ToggleAreaAsync(string areaId)
    {
        if (!_expandedAreas.Add(areaId))
        {
            _expandedAreas.Remove(areaId);
            return;
        }

        string key = GetDetailKey(areaId);
        if (_details.ContainsKey(key) || _loadingDetails.Contains(key))
            return;

        await LoadAreaDetailsAsync(areaId, Recipe is null);
    }

    /// <summary>
    /// Loads one selected area category into the component cache.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <param name="forceRefresh">Whether to bypass the connection cache.</param>
    /// <returns>A task representing detail loading.</returns>
    private async Task LoadAreaDetailsAsync(string areaId, bool forceRefresh)
    {
        string key = GetDetailKey(areaId);
        _loadingDetails.Add(key);
        _detailErrors.Remove(key);
        try
        {
            AreaLookupDetailResponsePayload response = await Connection.GetAreaDetailsAsync(areaId, _selectedCategory, Recipe, forceRefresh);
            _details[key] = response;
            foreach (AreaEncounterEntryPayload encounter in response.Encounters.Where(encounter => encounter.DetailsRevealed))
                _encounterSpriteSources[encounter.EntryId] = LocalSpriteLoader.Load(GameRoot, encounter.SpritePath);

            foreach (AreaTrainerEntryPayload trainer in response.Trainers.Where(trainer => trainer.DetailsRevealed))
            {
                foreach (AreaTrainerPokemonPayload pokemon in trainer.Party)
                    _trainerSpriteSources[GetTrainerPokemonKey(trainer, pokemon)] = LocalSpriteLoader.Load(GameRoot, pokemon.SpritePath);
            }
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            _detailErrors[key] = exception.Message;
        }
        finally
        {
            _loadingDetails.Remove(key);
        }
    }

    /// <summary>
    /// Loads the selected category's area list and tracker-owned completion counts.
    /// </summary>
    /// <param name="forceRefresh">Whether to request the game area list again.</param>
    /// <returns>A task representing the area-list request.</returns>
    private async Task LoadSummariesAsync(bool forceRefresh)
    {
        if (_loadingSummaries)
            return;

        _loadingSummaries = true;
        try
        {
            AreaLookupSummaryResponsePayload response = await Connection.GetAreaSummariesAsync(_selectedCategory, Recipe, forceRefresh);
            _areas = response.Areas;
        }
        catch (Exception exception) when (IsExpectedRequestException(exception))
        {
            _error = exception.Message;
        }
        finally
        {
            _loadingSummaries = false;
        }
    }

    /// <summary>
    /// Refreshes counts and an expanded matching area after background discovery persistence.
    /// </summary>
    /// <param name="sender">The discovery store.</param>
    /// <param name="args">The persisted discovery change.</param>
    private void HandleDiscoveryChanged(object? sender, AreaDiscoveryChangedEventArgs args)
    {
        if (args.RunId != RunId)
            return;

        _ = InvokeAsync(async () =>
        {
            await LoadSummariesAsync(false);
            if (args.Category == _selectedCategory && _expandedAreas.Contains(args.AreaId)
                && !_loadingDetails.Contains(GetDetailKey(args.AreaId)))
                await LoadAreaDetailsAsync(args.AreaId, true);

            StateHasChanged();
        });
    }

    /// <summary>
    /// Gets areas containing at least one entry in the selected category.
    /// </summary>
    /// <returns>The visible area summaries.</returns>
    private IEnumerable<AreaSummaryPayload> GetVisibleAreas()
        => _areas.Where(area => GetTotal(area) > 0);

    /// <summary>
    /// Gets the localized selected-category progress for one area.
    /// </summary>
    /// <param name="area">The area summary.</param>
    /// <returns>The completed and total count.</returns>
    private string GetProgressText(AreaSummaryPayload area)
    {
        return _selectedCategory switch
        {
            AreaContentCategory.Trainer => Text["Lookup.Areas.DefeatedProgress", area.TrainerDefeated, area.TrainerTotal],
            AreaContentCategory.Encounter => Text["Lookup.Areas.EncounteredProgress", area.Encountered, area.EncounterTotal],
            AreaContentCategory.Item => Text["Lookup.Areas.CollectedProgress", area.ItemsCollected, area.ItemTotal],
            _ => string.Empty
        };
    }

    /// <summary>
    /// Gets the entry total for the selected category.
    /// </summary>
    /// <param name="area">The area summary.</param>
    /// <returns>The entry total.</returns>
    private int GetTotal(AreaSummaryPayload area)
    {
        return _selectedCategory switch
        {
            AreaContentCategory.Trainer => area.TrainerTotal,
            AreaContentCategory.Encounter => area.EncounterTotal,
            AreaContentCategory.Item => area.ItemTotal,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the localized category name.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The category name.</returns>
    private string GetCategoryName(AreaContentCategory category)
    {
        return category switch
        {
            AreaContentCategory.Trainer => Text["Lookup.Areas.Trainers"],
            AreaContentCategory.Encounter => Text["Lookup.Areas.Encounters"],
            AreaContentCategory.Item => Text["Lookup.Areas.Items"],
            _ => string.Empty
        };
    }

    /// <summary>
    /// Gets the visual classes for one category tab.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The tab classes.</returns>
    private string GetCategoryTabClass(AreaContentCategory category)
        => category == _selectedCategory ? "area-category-tab selected" : "area-category-tab";

    /// <summary>
    /// Gets whether an area is expanded in the selected category.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>Whether the area is expanded.</returns>
    private bool IsExpanded(string areaId)
        => _expandedAreas.Contains(areaId);

    /// <summary>
    /// Builds the in-memory detail key for the selected category and area.
    /// </summary>
    /// <param name="areaId">The stable logical area identifier.</param>
    /// <returns>The detail key.</returns>
    private string GetDetailKey(string areaId)
        => $"{_selectedCategory}:{areaId}";

    /// <summary>
    /// Formats an encounter's level or inclusive level range.
    /// </summary>
    /// <param name="encounter">The encounter entry.</param>
    /// <returns>The localized level text.</returns>
    private string FormatEncounterRange(AreaEncounterEntryPayload encounter)
    {
        return encounter.MinimumLevel == encounter.MaximumLevel
            ? Text["Lookup.Areas.Level", encounter.MinimumLevel]
            : Text["Lookup.Areas.LevelRange", encounter.MinimumLevel, encounter.MaximumLevel];
    }

    /// <summary>
    /// Gets the disclosed encounter identity with a stable identifier fallback.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    /// <returns>The species name or stable species identifier.</returns>
    private static string GetEncounterIdentity(AreaEncounterEntryPayload encounter)
        => encounter.SpeciesName ?? encounter.SpeciesId ?? "—";

    /// <summary>
    /// Gets the locally loaded icon for a disclosed encounter.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    /// <returns>The WebView sprite source, or null.</returns>
    private string? GetEncounterSpriteSource(AreaEncounterEntryPayload encounter)
        => _encounterSpriteSources.GetValueOrDefault(encounter.EntryId);

    /// <summary>
    /// Builds the local sprite-cache key for a disclosed trainer party slot.
    /// </summary>
    /// <param name="trainer">The owning trainer entry.</param>
    /// <param name="pokemon">The disclosed party slot.</param>
    /// <returns>The stable in-memory cache key.</returns>
    private static string GetTrainerPokemonKey(AreaTrainerEntryPayload trainer, AreaTrainerPokemonPayload pokemon)
        => $"{trainer.EntryId}:{pokemon.Slot}";

    /// <summary>
    /// Gets the locally loaded icon for a disclosed trainer party slot.
    /// </summary>
    /// <param name="trainer">The owning trainer entry.</param>
    /// <param name="pokemon">The disclosed party slot.</param>
    /// <returns>The WebView sprite source, or null.</returns>
    private string? GetTrainerSpriteSource(AreaTrainerEntryPayload trainer, AreaTrainerPokemonPayload pokemon)
        => _trainerSpriteSources.GetValueOrDefault(GetTrainerPokemonKey(trainer, pokemon));

    /// <summary>
    /// Opens the enlarged local sprite for one disclosed encounter.
    /// </summary>
    /// <param name="encounter">The disclosed encounter entry.</param>
    private void OpenEncounterSprite(AreaEncounterEntryPayload encounter)
    {
        OpenSprite(GetEncounterSpriteSource(encounter), GetEncounterIdentity(encounter));
    }

    /// <summary>
    /// Opens the enlarged sprite dialog.
    /// </summary>
    /// <param name="source">The loaded sprite source.</param>
    /// <param name="label">The accessible Pokemon label.</param>
    private void OpenSprite(string? source, string label)
    {
        if (source is null)
            return;

        _enlargedSpriteSource = source;
        _enlargedSpriteLabel = label;
    }

    /// <summary>
    /// Closes the enlarged Pokemon sprite.
    /// </summary>
    private void CloseSprite()
    {
        _enlargedSpriteSource = null;
        _enlargedSpriteLabel = null;
    }

    /// <summary>
    /// Closes the enlarged sprite when Escape is pressed.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    private void HandleSpriteDialogKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape")
            CloseSprite();
    }

    /// <summary>
    /// Gets whether an exception represents an expected lookup request failure.
    /// </summary>
    /// <param name="exception">The request exception.</param>
    /// <returns>Whether the exception can be displayed in the lookup interface.</returns>
    private static bool IsExpectedRequestException(Exception exception)
        => exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException;

    /// <summary>
    /// Removes tracker discovery subscriptions when the component is disposed.
    /// </summary>
    public void Dispose()
        => Discoveries.Changed -= HandleDiscoveryChanged;
}
