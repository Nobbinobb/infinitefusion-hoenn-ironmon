using System.Collections.Frozen;

namespace Ironmon.Tracker.Access;

/// <summary>
/// Provides the authoritative Ironmon diagnostic capability catalog and implication rules.
/// </summary>
public static class DiagnosticCapabilityCatalog
{
    private static readonly FrozenDictionary<string, DiagnosticCapabilityDefinition> Definitions = CreateDefinitions();

    /// <summary>
    /// Gets every supported capability in stable identifier order.
    /// </summary>
    public static IReadOnlyList<DiagnosticCapabilityDefinition> All { get; } = [.. Definitions.Values.OrderBy(definition => definition.Id, StringComparer.Ordinal)];

    /// <summary>
    /// Gets every supported capability identifier in stable order.
    /// </summary>
    public static IReadOnlySet<string> KnownIds { get; } = Definitions.Keys.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Determines whether a capability identifier is supported.
    /// </summary>
    /// <param name="capability">The capability identifier.</param>
    /// <returns>Whether the identifier belongs to the catalog.</returns>
    public static bool IsKnown(string capability)
        => !string.IsNullOrWhiteSpace(capability) && Definitions.ContainsKey(capability);

    /// <summary>
    /// Gets one capability definition.
    /// </summary>
    /// <param name="capability">The capability identifier.</param>
    /// <returns>The matching definition.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the capability is unsupported.</exception>
    public static DiagnosticCapabilityDefinition Get(string capability)
        => Definitions[capability];

    /// <summary>
    /// Expands known direct grants through every transitive implication rule.
    /// </summary>
    /// <param name="directCapabilities">The directly granted capability identifiers.</param>
    /// <returns>The stable, duplicate-free effective capability identifiers.</returns>
    /// <exception cref="ArgumentNullException">Thrown when directCapabilities is null.</exception>
    public static IReadOnlyList<string> Expand(IEnumerable<string> directCapabilities)
    {
        ArgumentNullException.ThrowIfNull(directCapabilities);
        HashSet<string> effective = new(StringComparer.Ordinal);
        Stack<string> remaining = new(directCapabilities.Where(IsKnown));
        while (remaining.TryPop(out string? capability))
        {
            if (!effective.Add(capability))
                continue;

            foreach (string included in Definitions[capability].IncludedCapabilities)
                remaining.Push(included);
        }

        return [.. effective.Order(StringComparer.Ordinal)];
    }

    /// <summary>
    /// Creates the fixed capability definition set.
    /// </summary>
    /// <returns>The keyed immutable capability definitions.</returns>
    private static FrozenDictionary<string, DiagnosticCapabilityDefinition> CreateDefinitions()
    {
        DiagnosticCapabilityDefinition[] definitions =
        [
            new(DiagnosticCapabilities.DevelopmentAutoRevive, "NoFail / AutoRevive", "Keep the attempt active and revive the player Pokemon after a defeat or draw."),
            new(DiagnosticCapabilities.DevelopmentChangeAbility, "Change ability", "Assign any ability from the complete game catalog to the current player Pokemon."),
            new(DiagnosticCapabilities.DevelopmentChangeMoves, "Change moves", "Replace the current player Pokemon's move set."),
            new(DiagnosticCapabilities.DevelopmentEvolution, "Evolve / devolve", "Move the current player Pokemon across one direct generated evolution edge."),
            new(DiagnosticCapabilities.DevelopmentFullHeal, "Full heal", "Restore the current player Pokemon's HP, status, and PP outside battle."),
            new(DiagnosticCapabilities.DevelopmentGiveItem, "Give item", "Add a selected item to the player's Bag outside battle."),
            new(DiagnosticCapabilities.DevelopmentLevel, "Level adjustment", "Set the current player Pokemon's level outside battle."),
            new(DiagnosticCapabilities.DevelopmentSwapPokemon, "Swap Pokemon", "Replace the current player Pokemon with a selected active-run lookup species at the same level."),
            new(DiagnosticCapabilities.PokemonCurrentPlayer, "Current player", "Inspect the current live player Pokemon."),
            new(DiagnosticCapabilities.PokemonCurrentEnemies, "Current enemies", "Inspect current live enemy Pokemon."),
            new(DiagnosticCapabilities.PokemonAllActive, "All active-run Pokemon", "Search and inspect any Pokemon generated for the active run.", [DiagnosticCapabilities.PokemonCurrentPlayer, DiagnosticCapabilities.PokemonCurrentEnemies]),
            new(DiagnosticCapabilities.PokemonOverview, "Pokemon overview", "View identity, form, level, held item, fusion components, and active ability context."),
            new(DiagnosticCapabilities.PokemonAbilities, "Generated abilities", "View original and generated ability slots and fusion-source diagnostics."),
            new(DiagnosticCapabilities.PokemonBaseStats, "Generated base stats", "View original and generated base stats and totals."),
            new(DiagnosticCapabilities.PokemonMoveAccess, "Generated move access", "View generated Learnset, Egg, machine, and supported Tutor access."),
            new(DiagnosticCapabilities.EvolutionResults, "Evolution results", "View exact generated evolution results and graphs."),
            new(DiagnosticCapabilities.EvolutionCandidates, "Evolution candidates", "View valid generated evolution candidate pools."),
            new(DiagnosticCapabilities.EvolutionGeneratorDetails, "Evolution generator details", "View evolution rules, versions, and fingerprints."),
            new(DiagnosticCapabilities.WorldWildEncounters, "Wild encounters", "View undiscovered generated wild mappings and reverse occurrences."),
            new(DiagnosticCapabilities.WorldTrainerParties, "Trainer parties", "View undefeated generated trainer parties and reverse occurrences."),
            new(DiagnosticCapabilities.WorldItems, "Ground items", "View generated results for uncollected ground items."),
            new(DiagnosticCapabilities.FusionPreviewResults, "Fusion previews", "Preview both generated orientations for selected normal Pokemon."),
            new(DiagnosticCapabilities.FusionMaterialPairs, "Fusion material pairs", "View ordered material pairs that generate a selected fusion."),
            new(DiagnosticCapabilities.RunConfiguration, "Run configuration", "View active randomization and unfusion configuration."),
            new(DiagnosticCapabilities.RunSeed, "Run seed", "View the active run seed."),
            new(DiagnosticCapabilities.RunGeneratorManifests, "Generator manifests", "View generator versions, pools, fingerprints, and mapping counts."),
            new(DiagnosticCapabilities.TrackerProtocolHistory, "Protocol history", "View tracker connection lifecycle, errors, requests, and responses."),
            new(DiagnosticCapabilities.TrackerRawState, "Raw tracker state", "View raw current connection and tracker-run state."),
            new(DiagnosticCapabilities.TrackerPersistedKnowledge, "Persisted knowledge", "View raw persisted move-discovery knowledge.")
        ];

        return definitions.ToFrozenDictionary(definition => definition.Id, StringComparer.Ordinal);
    }
}
