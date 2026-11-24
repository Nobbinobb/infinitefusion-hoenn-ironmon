using Ironmon.Tracker.UI.Components;

namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Provides the localized presentation groups used by the capability form.
/// </summary>
public static class GeneratorCapabilityGroupCatalog
{
    /// <summary>
    /// Gets every generator capability group in display order.
    /// </summary>
    public static IReadOnlyList<GeneratorCapabilityGroup> All { get; } = Array.AsReadOnly<GeneratorCapabilityGroup>(
    [
        new("Generator.CapabilityGroups.PokemonAvailability",
        (string[])[
            DiagnosticCapabilities.PokemonCurrentPlayer,
            DiagnosticCapabilities.PokemonCurrentEnemies,
            DiagnosticCapabilities.PokemonAllActive
        ], ObsidianIconKind.Crosshair),
        new("Generator.CapabilityGroups.PokemonInformation",
        (string[])[
            DiagnosticCapabilities.PokemonOverview,
            DiagnosticCapabilities.PokemonAbilities,
            DiagnosticCapabilities.PokemonBaseStats,
            DiagnosticCapabilities.PokemonMoveAccess
        ], ObsidianIconKind.Layers),
        new("Generator.CapabilityGroups.EvolutionInformation",
        (string[])[
            DiagnosticCapabilities.EvolutionResults,
            DiagnosticCapabilities.EvolutionCandidates,
            DiagnosticCapabilities.EvolutionGeneratorDetails
        ], ObsidianIconKind.Branch),
        new("Generator.CapabilityGroups.AreaInformation",
        (string[])[
            DiagnosticCapabilities.WorldWildEncounters,
            DiagnosticCapabilities.WorldTrainerParties,
            DiagnosticCapabilities.WorldItems
        ], ObsidianIconKind.Map),
        new("Generator.CapabilityGroups.FusionInformation",
        (string[])[
            DiagnosticCapabilities.FusionPreviewResults,
            DiagnosticCapabilities.FusionMaterialPairs
        ], ObsidianIconKind.Merge),
        new("Generator.CapabilityGroups.RunInformation",
        (string[])[
            DiagnosticCapabilities.RunConfiguration,
            DiagnosticCapabilities.RunSeed,
            DiagnosticCapabilities.RunGeneratorManifests
        ], ObsidianIconKind.Database),
        new("Generator.CapabilityGroups.TrackerDiagnostics",
        (string[])[
            DiagnosticCapabilities.TrackerProtocolHistory,
            DiagnosticCapabilities.TrackerRawState,
            DiagnosticCapabilities.TrackerPersistedKnowledge
        ], ObsidianIconKind.Chart),
        new("Generator.CapabilityGroups.DevelopmentControls",
        (string[])[
            DiagnosticCapabilities.DevelopmentAutoRevive,
            DiagnosticCapabilities.DevelopmentFullHeal,
            DiagnosticCapabilities.DevelopmentLevel,
            DiagnosticCapabilities.DevelopmentChangeAbility,
            DiagnosticCapabilities.DevelopmentChangeMoves,
            DiagnosticCapabilities.DevelopmentGiveItem,
            DiagnosticCapabilities.DevelopmentEvolution,
            DiagnosticCapabilities.DevelopmentSwapPokemon
        ], ObsidianIconKind.Wrench)
    ]);
}
