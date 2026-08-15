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
            "Prefills every capability currently supported by the generator.",
            Sort(DiagnosticCapabilityCatalog.KnownIds.Where(capability => capability is not DiagnosticCapabilities.PokemonCurrentPlayer and not DiagnosticCapabilities.PokemonCurrentEnemies)))
    ];

    /// <summary>
    /// Sorts one preset capability set into canonical token order.
    /// </summary>
    /// <param name="capabilities">The capability identifiers.</param>
    /// <returns>The stable, duplicate-free capability list.</returns>
    private static IReadOnlyList<string> Sort(IEnumerable<string> capabilities)
        => [.. capabilities.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
