using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Lookup;

/// <summary>
/// Renders complete deterministic information returned by a post-run lookup.
/// </summary>
public partial class PokemonLookupCard
{
    private readonly Dictionary<PokemonInformationPage, PokemonLookupSnapshot> _sections = [];
    private PokemonInformationPage _selectedPage = PokemonInformationPage.Overview;
    private PokemonInformationPage? _loadingPage;
    private string? _observedSpeciesId;
    private string? _sectionError;
    private string? _obtainabilityError;
    private string? _spriteKey;
    private string? _spriteSource;
    private AbilitySnapshot? _selectedAbility;
    private PokemonObtainabilitySnapshot _obtainability = new();
    private PokemonObtainabilityResponsePayload? _obtainabilityProgress;
    private bool _checkingObtainability;
    private bool _evolutionGraphOpen;

    /// <summary>
    /// Gets or sets the request client used to load a selected information section.
    /// </summary>
    [Inject]
    private TrackerRequestClient Connection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the reconstructed Pokemon information.
    /// </summary>
    [Parameter]
    public PokemonLookupSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets or sets optional live instance diagnostics to merge into the shared pages.
    /// </summary>
    [Parameter]
    public DebugPokemonInspectorSnapshot? Inspector { get; set; }

    /// <summary>
    /// Gets or sets the completed-run reconstruction recipe.
    /// </summary>
    [Parameter]
    public CompletedRunRecipePayload? Recipe { get; set; }

    /// <summary>
    /// Gets or sets whether the card represents an authorized active-run lookup.
    /// </summary>
    [Parameter]
    public bool DebugMode { get; set; }

    /// <summary>
    /// Gets or sets the optional live source that owns the represented active-run Pokemon.
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
    /// Gets or sets the callback invoked when an information page is selected.
    /// </summary>
    [Parameter]
    public EventCallback<PokemonInformationPage> InformationPageSelected { get; set; }

    /// <summary>
    /// Refreshes the local sprite when the lookup result changes.
    /// </summary>
    protected override async Task OnParametersSetAsync()
    {
        bool speciesChanged = !string.Equals(_observedSpeciesId, Pokemon.Identity.SpeciesId, StringComparison.Ordinal);
        if (speciesChanged)
        {
            _observedSpeciesId = Pokemon.Identity.SpeciesId;
            _sections.Clear();
            _selectedAbility = null;
            _evolutionGraphOpen = false;
            _sectionError = null;
            _obtainabilityError = null;
            _obtainability = Pokemon.Identity.Obtainability;
            _obtainabilityProgress = null;
            _checkingObtainability = false;
            PokemonInformationPage incomingPage = (PokemonInformationPage)(int)Pokemon.Section;
            _selectedPage = CanShowPage(incomingPage) ? incomingPage : GetFirstVisiblePage();
        }

        PokemonInformationPage receivedPage = (PokemonInformationPage)(int)Pokemon.Section;
        _sections[receivedPage] = Pokemon;
        if (_loadingPage == receivedPage)
            _loadingPage = null;
        string? key = GameRoot is null || Pokemon.Identity.SpritePath is null ? null : $"{GameRoot}|{Pokemon.Identity.SpritePath}";
        if (key != _spriteKey)
        {
            _spriteKey = key;
            _spriteSource = LocalSpriteLoader.Load(GameRoot, Pokemon.Identity.SpritePath);
        }

        if (speciesChanged && !_sections.ContainsKey(_selectedPage))
            await LoadSectionAsync(_selectedPage);
    }

    /// <summary>
    /// Gets the snapshot for the selected section, falling back to the overview identity while it loads.
    /// </summary>
    /// <returns>The selected section snapshot.</returns>
    private PokemonLookupSnapshot ActivePokemon
        => _sections.GetValueOrDefault(_selectedPage) ?? Pokemon;

    /// <summary>
    /// Gets the selected Abilities section.
    /// </summary>
    private PokemonLookupAbilitiesSnapshot AbilitySection
        => ActivePokemon.Abilities ?? throw new InvalidOperationException("The Abilities lookup response is missing its section payload.");

    /// <summary>
    /// Gets the selected Stats section.
    /// </summary>
    private PokemonLookupStatsSnapshot StatsSection
        => ActivePokemon.Stats ?? throw new InvalidOperationException("The Stats lookup response is missing its section payload.");

    /// <summary>
    /// Gets the required live inspector Stats section.
    /// </summary>
    private DebugPokemonStatsSnapshot InspectorStats
        => Inspector?.Stats ?? throw new InvalidOperationException("The live inspector response is missing its Stats section payload.");

    /// <summary>
    /// Gets the selected Moves section.
    /// </summary>
    private PokemonLookupMovesSnapshot MovesSection
        => ActivePokemon.Moves ?? throw new InvalidOperationException("The Moves lookup response is missing its section payload.");

    /// <summary>
    /// Gets the selected Evolutions section.
    /// </summary>
    private PokemonLookupEvolutionsSnapshot EvolutionSection
        => ActivePokemon.Evolutions ?? throw new InvalidOperationException("The Evolutions lookup response is missing its section payload.");

    /// <summary>
    /// Gets whether the represented species is a fusion without requiring Overview relationship data.
    /// </summary>
    /// <returns>Whether the represented species is a fusion.</returns>
    private bool IsFusion()
        => Inspector?.Identity.Fusion ?? ActivePokemon.Identity.Fusion;

    /// <summary>
    /// Gets the localized obtainability state shown beside the selected Pokemon.
    /// </summary>
    private string ObtainabilityLabel => _obtainability.Status switch
    {
        PokemonObtainabilityStatus.Obtainable => Text["Lookup.Obtainability.Obtainable"],
        PokemonObtainabilityStatus.Unobtainable => Text["Lookup.Obtainability.Unobtainable"],
        PokemonObtainabilityStatus.Calculating => Text["Lookup.Obtainability.Calculating"],
        _ => Text["Lookup.Obtainability.Unknown"]
    };

    /// <summary>
    /// Gets the visual class for the current obtainability state.
    /// </summary>
    private string ObtainabilityCssClass => $"obtainability-state {_obtainability.Status.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Advances the shared run calculation until this Pokemon is proven or the run is complete.
    /// </summary>
    /// <returns>A task representing the progressive calculation.</returns>
    private async Task CheckObtainabilityAsync()
    {
        if (_checkingObtainability || Recipe is null || DebugMode)
            return;

        string speciesId = Pokemon.Identity.SpeciesId;
        _checkingObtainability = true;
        _obtainabilityError = null;
        try
        {
            while (true)
            {
                PokemonObtainabilityResponsePayload response = await Connection.AdvancePokemonObtainabilityAsync(Recipe, speciesId, foreground: true);
                if (!string.Equals(Pokemon.Identity.SpeciesId, speciesId, StringComparison.Ordinal))
                    return;

                _obtainabilityProgress = response;
                if (response.Target is not null)
                    _obtainability = response.Target;

                await InvokeAsync(StateHasChanged);
                if (response.Complete || _obtainability.Status == PokemonObtainabilityStatus.Obtainable)
                    break;

                await Task.Delay(50);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _obtainabilityError = exception.Message;
        }
        finally
        {
            _checkingObtainability = false;
        }
    }

    /// <summary>
    /// Creates the selected Pokemon's graph node.
    /// </summary>
    /// <returns>The current generated graph node.</returns>
    private EvolutionTargetSnapshot GetCurrentEvolutionNode() => new()
    {
        SpeciesId = ActivePokemon.Identity.SpeciesId,
        SpeciesName = ActivePokemon.Identity.SpeciesName,
        SpritePath = ActivePokemon.Identity.SpritePath,
        BaseStatTotal = EvolutionSection.CurrentBaseStatTotal,
        StageLevel = EvolutionSection.CurrentStageLevel
    };

    /// <summary>
    /// Selects one shared Pokemon information page.
    /// </summary>
    /// <param name="page">The requested page.</param>
    private async Task SelectPageAsync(PokemonInformationPage page)
    {
        if (!CanShowPage(page))
            return;

        _selectedPage = page;
        _selectedAbility = null;
        _evolutionGraphOpen = false;
        _sectionError = null;
        if (DebugMode && Inspector is not null)
        {
            _loadingPage = page;
            await InformationPageSelected.InvokeAsync(page);
        }
        else
        {
            await Task.WhenAll(InformationPageSelected.InvokeAsync(page), LoadSectionAsync(page));
        }
    }

    /// <summary>
    /// Loads one information section once for the current Pokemon.
    /// </summary>
    /// <param name="page">The requested page.</param>
    /// <returns>A task representing the request.</returns>
    private async Task LoadSectionAsync(PokemonInformationPage page)
    {
        if (_sections.ContainsKey(page) || _loadingPage == page)
            return;

        if (DebugMode && page == PokemonInformationPage.Overview && !Connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonOverview))
            return;

        if (DebugMode && page == PokemonInformationPage.Evolutions && !Connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionResults))
            return;

        if (!DebugMode && Recipe is null)
            return;

        string speciesId = Pokemon.Identity.SpeciesId;
        _loadingPage = page;
        try
        {
            PokemonLookupSection section = (PokemonLookupSection)(int)page;
            PokemonLookupSnapshot snapshot = DebugMode
                ? await Connection.LookupDebugPokemonAsync(speciesId, section)
                : await Connection.LookupPokemonAsync(Recipe!, speciesId, section);

            if (string.Equals(Pokemon.Identity.SpeciesId, speciesId, StringComparison.Ordinal))
                _sections[page] = snapshot;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or TimeoutException or TrackerProtocolException)
        {
            _sectionError = exception.Message;
        }
        finally
        {
            if (_loadingPage == page)
                _loadingPage = null;
        }
    }

    /// <summary>
    /// Gets the selected CSS class for one shared information page.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>The page button CSS classes.</returns>
    private string GetPageClass(PokemonInformationPage page)
        => page == _selectedPage ? TrackerUiConstants.SelectedCssClass : string.Empty;

    /// <summary>
    /// Gets whether one shared information page has authorized content.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>Whether the page should be visible.</returns>
    private bool CanShowPage(PokemonInformationPage page)
    {
        if (!DebugMode)
            return true;

        return page switch
        {
            PokemonInformationPage.Overview => TrackerDiagnosticCapabilityRules.HasAnyOverviewSurface(Connection),
            PokemonInformationPage.Evolutions => TrackerDiagnosticCapabilityRules.HasAnyEvolutionSurface(Connection),
            _ => Connection.HasDiagnosticCapability(TrackerDiagnosticCapabilityRules.GetPokemonInformationCapability(page))
        };
    }

    /// <summary>
    /// Gets the first visible information page in stable UI order.
    /// </summary>
    /// <returns>The first visible page, or Overview when none is available.</returns>
    private PokemonInformationPage GetFirstVisiblePage()
        => Enum.GetValues<PokemonInformationPage>().FirstOrDefault(CanShowPage);

    /// <summary>
    /// Gets the localized label for one shared information page.
    /// </summary>
    /// <param name="page">The represented page.</param>
    /// <returns>The localized page label.</returns>
    private string GetPageText(PokemonInformationPage page)
    {
        return page switch
        {
            PokemonInformationPage.Overview => Text["Debug.Inspector.Overview"],
            PokemonInformationPage.Abilities => Text["Debug.Inspector.Abilities"],
            PokemonInformationPage.Stats => Text["Debug.Inspector.Stats"],
            PokemonInformationPage.Moves => Text["Debug.Inspector.Moves"],
            PokemonInformationPage.Evolutions => Text["Debug.Inspector.Evolutions"],
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "The Pokemon information page is unsupported.")
        };
    }

    /// <summary>
    /// Gets whether exact generated evolution results are visible.
    /// </summary>
    private bool HasEvolutionResults
        => !DebugMode || Connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionResults);

    /// <summary>
    /// Gets whether generated evolution candidate pools are visible.
    /// </summary>
    private bool HasEvolutionCandidates
        => !DebugMode || Connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionCandidates);

    /// <summary>
    /// Gets whether evolution-generator metadata is visible.
    /// </summary>
    private bool HasEvolutionGeneratorDetails
        => !DebugMode || Connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionGeneratorDetails);

    /// <summary>
    /// Gets live ability-slot diagnostics when available, otherwise the reconstructed lookup diagnostics.
    /// </summary>
    /// <returns>The slot diagnostics shown by the shared Abilities page.</returns>
    private IReadOnlyList<DebugAbilitySlotSnapshot> GetAbilitySlots()
        => Inspector?.Abilities?.Slots ?? AbilitySection.Slots;

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

    /// <summary>
    /// Opens full information for one generated ability.
    /// </summary>
    /// <param name="ability">The selected ability.</param>
    private void SelectAbility(AbilitySnapshot ability)
        => _selectedAbility = ability;

    /// <summary>
    /// Closes the generated ability information.
    /// </summary>
    private void CloseAbility()
        => _selectedAbility = null;

    /// <summary>
    /// Opens the progressively loaded generated evolution graph.
    /// </summary>
    private void OpenEvolutionGraph()
        => _evolutionGraphOpen = true;

    /// <summary>
    /// Closes the generated evolution graph.
    /// </summary>
    private void CloseEvolutionGraph()
        => _evolutionGraphOpen = false;
}
