namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Provides local editable templates for common diagnostic investigations.
/// </summary>
public static class DiagnosticAccessPresetCatalog
{
    /// <summary>
    /// Gets every built-in preset in display order.
    /// </summary>
    public static IReadOnlyList<DiagnosticAccessPreset> All { get; } =
    [
        new(
            DiagnosticAccessPresetIds.ProtocolProblem,
            "Protocol problem",
            "Prefills tracker connection history and raw state diagnostics.",
            Sort([DiagnosticCapabilities.TrackerProtocolHistory, DiagnosticCapabilities.TrackerRawState])),
        new(
            DiagnosticAccessPresetIds.AbilityGeneration,
            "Ability generation",
            "Prefills current Pokemon ability and run-generator diagnostics.",
            Sort(
            [
                DiagnosticCapabilities.PokemonCurrentEnemies,
                DiagnosticCapabilities.PokemonCurrentPlayer,
                DiagnosticCapabilities.PokemonOverview,
                DiagnosticCapabilities.PokemonAbilities,
                DiagnosticCapabilities.RunConfiguration,
                DiagnosticCapabilities.RunGeneratorManifests,
                DiagnosticCapabilities.RunSeed
            ])),
        new(
            DiagnosticAccessPresetIds.EncounterMapping,
            "Encounter mapping",
            "Prefills active-run Pokemon, wild, trainer, seed, and generator information.",
            Sort(
            [
                DiagnosticCapabilities.PokemonAllActive,
                DiagnosticCapabilities.PokemonOverview,
                DiagnosticCapabilities.RunConfiguration,
                DiagnosticCapabilities.RunGeneratorManifests,
                DiagnosticCapabilities.RunSeed,
                DiagnosticCapabilities.WorldTrainerParties,
                DiagnosticCapabilities.WorldWildEncounters
            ])),
        new(
            DiagnosticAccessPresetIds.FullCurrentDebug,
            "Full current debug access",
            "Prefills every read-only diagnostic capability currently supported by the generator. Development mutations remain opt-in.",
            Sort(DiagnosticCapabilityCatalog.KnownIds.Where(IsReadOnlyFullDebugCapability)))
    ];

    /// <summary>
    /// Determines whether one capability belongs in the historical read-only full-debug preset.
    /// </summary>
    /// <param name="capability">The candidate capability identifier.</param>
    /// <returns><see langword="true"/> when the capability is neither a direct active-Pokémon grant nor a development action; otherwise, <see langword="false"/>.</returns>
    private static bool IsReadOnlyFullDebugCapability(string capability)
    {
        return capability is not DiagnosticCapabilities.PokemonCurrentPlayer
            and not DiagnosticCapabilities.PokemonCurrentEnemies
            && !capability.StartsWith("development.", StringComparison.Ordinal);
    }

    /// <summary>
    /// Sorts one preset capability set into canonical token order.
    /// </summary>
    /// <param name="capabilities">The capability identifiers.</param>
    /// <returns>The stable, duplicate-free capability list.</returns>
    private static IReadOnlyList<string> Sort(IEnumerable<string> capabilities)
        => [.. capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
