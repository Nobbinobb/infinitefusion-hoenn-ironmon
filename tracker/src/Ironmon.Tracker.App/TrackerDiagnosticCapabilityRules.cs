using Ironmon.Tracker.App.Components.Lookup;

namespace Ironmon.Tracker.App;

/// <summary>
/// Maps tracker diagnostic surfaces to their stable capability requirements.
/// </summary>
public static class TrackerDiagnosticCapabilityRules
{
    /// <summary>
    /// Gets the Pokemon information capability represented by one shared page.
    /// </summary>
    /// <param name="page">The shared Pokemon information page.</param>
    /// <returns>The stable information capability.</returns>
    public static string GetPokemonInformationCapability(PokemonInformationPage page)
    {
        return page switch
        {
            PokemonInformationPage.Overview => DiagnosticCapabilities.PokemonOverview,
            PokemonInformationPage.Abilities => DiagnosticCapabilities.PokemonAbilities,
            PokemonInformationPage.Stats => DiagnosticCapabilities.PokemonBaseStats,
            PokemonInformationPage.Moves => DiagnosticCapabilities.PokemonMoveAccess,
            PokemonInformationPage.Evolutions => DiagnosticCapabilities.EvolutionResults,
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, "The Pokemon information page is unsupported.")
        };
    }

    /// <summary>
    /// Gets whether any independently selectable Pokemon information page is authorized.
    /// </summary>
    /// <param name="connection">The active game request client.</param>
    /// <returns>Whether at least one information page can be requested.</returns>
    public static bool HasAnyPokemonInformation(TrackerRequestClient connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return HasAnyOverviewSurface(connection)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonAbilities)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonBaseStats)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonMoveAccess)
            || HasAnyEvolutionSurface(connection);
    }

    /// <summary>
    /// Gets whether the active-run Overview surface has any authorized content.
    /// </summary>
    /// <param name="connection">The active game request client.</param>
    /// <returns>Whether Overview or one of its independent tools is authorized.</returns>
    public static bool HasAnyOverviewSurface(TrackerRequestClient connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonOverview)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.WorldWildEncounters)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.WorldTrainerParties)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.FusionMaterialPairs)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.FusionPreviewResults);
    }

    /// <summary>
    /// Gets whether active-run Evolutions has exact results or candidate-list access.
    /// </summary>
    /// <param name="connection">The active game request client.</param>
    /// <returns>Whether either independent evolution surface is authorized.</returns>
    public static bool HasAnyEvolutionSurface(TrackerRequestClient connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionResults)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionCandidates);
    }

    /// <summary>
    /// Gets whether the complete active-run obtainability information domain is authorized.
    /// </summary>
    /// <param name="connection">The active game request client.</param>
    /// <returns>Whether every capability required by active-run obtainability is available.</returns>
    public static bool HasRunObtainability(TrackerRequestClient connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionResults)
            && connection.HasDiagnosticCapability(DiagnosticCapabilities.FusionMaterialPairs)
            && connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonAllActive)
            && connection.HasDiagnosticCapability(DiagnosticCapabilities.WorldItems)
            && connection.HasDiagnosticCapability(DiagnosticCapabilities.WorldWildEncounters);
    }

    /// <summary>
    /// Gets whether arbitrary active-run Pokemon lookup has any useful authorized surface.
    /// </summary>
    /// <param name="connection">The active game request client.</param>
    /// <returns>Whether search and at least one information or tool surface are authorized.</returns>
    public static bool CanUseActivePokemonLookup(TrackerRequestClient connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.HasDiagnosticCapability(DiagnosticCapabilities.PokemonAllActive)
            && HasAnyPokemonInformation(connection);
    }

    /// <summary>
    /// Gets whether any game-owned run diagnostic group is authorized.
    /// </summary>
    /// <param name="connection">The active game request client.</param>
    /// <returns>Whether at least one run diagnostic group is authorized.</returns>
    public static bool HasAnyRunDiagnostics(TrackerRequestClient connection)
    {
        ArgumentNullException.ThrowIfNull(connection);
        return connection.HasDiagnosticCapability(DiagnosticCapabilities.RunConfiguration)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.RunSeed)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.RunGeneratorManifests)
            || connection.HasDiagnosticCapability(DiagnosticCapabilities.EvolutionGeneratorDetails);
    }

    /// <summary>
    /// Gets whether any tracker-owned diagnostic group is locally authorized.
    /// </summary>
    /// <param name="access">The tracker-owned access snapshot.</param>
    /// <returns>Whether at least one tracker diagnostic group is authorized.</returns>
    public static bool HasAnyTrackerDiagnostics(DiagnosticAccessSnapshot access)
    {
        ArgumentNullException.ThrowIfNull(access);
        return access.HasCapability(DiagnosticCapabilities.TrackerProtocolHistory)
            || access.HasCapability(DiagnosticCapabilities.TrackerRawState)
            || access.HasCapability(DiagnosticCapabilities.TrackerPersistedKnowledge);
    }
}
