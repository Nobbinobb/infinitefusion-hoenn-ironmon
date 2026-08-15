namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Provides the localized presentation groups used by the capability form.
/// </summary>
public static class GeneratorCapabilityGroupCatalog
{
    /// <summary>
    /// Gets every generator capability group in display order.
    /// </summary>
    public static IReadOnlyList<GeneratorCapabilityGroup> All { get; } =
    [
        new("Generator.CapabilityGroups.PokemonAvailability",
        [
            DiagnosticCapabilities.PokemonCurrentPlayer,
            DiagnosticCapabilities.PokemonCurrentEnemies,
            DiagnosticCapabilities.PokemonAllActive
        ]),
        new("Generator.CapabilityGroups.PokemonInformation",
        [
            DiagnosticCapabilities.PokemonOverview,
            DiagnosticCapabilities.PokemonAbilities,
            DiagnosticCapabilities.PokemonBaseStats,
            DiagnosticCapabilities.PokemonMoveAccess
        ]),
        new("Generator.CapabilityGroups.EvolutionInformation",
        [
            DiagnosticCapabilities.EvolutionResults,
            DiagnosticCapabilities.EvolutionCandidates,
            DiagnosticCapabilities.EvolutionGeneratorDetails
        ]),
        new("Generator.CapabilityGroups.AreaInformation",
        [
            DiagnosticCapabilities.WorldWildEncounters,
            DiagnosticCapabilities.WorldTrainerParties,
            DiagnosticCapabilities.WorldItems
        ]),
        new("Generator.CapabilityGroups.FusionInformation",
        [
            DiagnosticCapabilities.FusionPreviewResults,
            DiagnosticCapabilities.FusionMaterialPairs
        ]),
        new("Generator.CapabilityGroups.RunInformation",
        [
            DiagnosticCapabilities.RunConfiguration,
            DiagnosticCapabilities.RunSeed,
            DiagnosticCapabilities.RunGeneratorManifests
        ]),
        new("Generator.CapabilityGroups.TrackerDiagnostics",
        [
            DiagnosticCapabilities.TrackerProtocolHistory,
            DiagnosticCapabilities.TrackerRawState,
            DiagnosticCapabilities.TrackerPersistedKnowledge
        ])
    ];
}
