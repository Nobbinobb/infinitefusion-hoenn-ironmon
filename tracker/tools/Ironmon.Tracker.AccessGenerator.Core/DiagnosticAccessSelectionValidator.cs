namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Validates generator selections that would otherwise produce unusable access tokens.
/// </summary>
public static class DiagnosticAccessSelectionValidator
{
    private static readonly IReadOnlySet<string> PokemonBoundCapabilities = new HashSet<string>(StringComparer.Ordinal)
    {
        DiagnosticCapabilities.EvolutionCandidates,
        DiagnosticCapabilities.EvolutionResults,
        DiagnosticCapabilities.FusionMaterialPairs,
        DiagnosticCapabilities.PokemonAbilities,
        DiagnosticCapabilities.PokemonBaseStats,
        DiagnosticCapabilities.PokemonMoveAccess,
        DiagnosticCapabilities.PokemonOverview
    };

    /// <summary>
    /// Gets every selection problem that must be corrected before generation.
    /// </summary>
    /// <param name="directCapabilities">The direct capability selection.</param>
    /// <returns>The user-facing validation problems.</returns>
    /// <exception cref="ArgumentNullException">Thrown when directCapabilities is null.</exception>
    public static IReadOnlyList<string> GetErrors(IEnumerable<string> directCapabilities)
    {
        ArgumentNullException.ThrowIfNull(directCapabilities);
        HashSet<string> direct = new(directCapabilities, StringComparer.Ordinal);
        IReadOnlyList<string> effective = DiagnosticCapabilityCatalog.Expand(direct);
        bool hasPokemonAvailability = effective.Contains(DiagnosticCapabilities.PokemonCurrentPlayer, StringComparer.Ordinal)
            || effective.Contains(DiagnosticCapabilities.PokemonCurrentEnemies, StringComparer.Ordinal);

        List<string> errors = [];
        if (direct.Count == 0)
            errors.Add("Select at least one diagnostic capability.");

        if (direct.Any(PokemonBoundCapabilities.Contains) && !hasPokemonAvailability)
            errors.Add("Pokemon information requires Current Player, Current Enemies, or All Active Pokemon.");

        if (direct.Contains(DiagnosticCapabilities.FusionPreviewResults) && !direct.Contains(DiagnosticCapabilities.PokemonAllActive))
            errors.Add("Fusion previews require All Active Pokemon.");

        return errors;
    }
}
