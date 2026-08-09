namespace Ironmon.Tracker.Protocol.Transport;

/// <summary>
/// Defines stable command identifiers sent from the tracker to the game.
/// </summary>
public static class TrackerCommands
{
    /// <summary>
    /// Gets the current-state recovery command.
    /// </summary>
    public const string CurrentState = "current_state";

    /// <summary>
    /// Gets the completed-run Pokemon search command.
    /// </summary>
    public const string PokemonSearch = "pokemon_search";

    /// <summary>
    /// Gets the completed-run Pokemon lookup command.
    /// </summary>
    public const string PokemonLookup = "pokemon_lookup";

    /// <summary>
    /// Gets the completed-run fusion preview command.
    /// </summary>
    public const string FusionPreview = "fusion_preview";

    /// <summary>
    /// Gets the authorized current-Pokemon inspection command.
    /// </summary>
    public const string DebugInspectPokemon = "debug_inspect_pokemon";

    /// <summary>
    /// Gets the authorized run-diagnostics command.
    /// </summary>
    public const string DebugRunDiagnostics = "debug_run_diagnostics";

    /// <summary>
    /// Gets the authorized active-run Pokemon search command.
    /// </summary>
    public const string DebugPokemonSearch = "debug_pokemon_search";

    /// <summary>
    /// Gets the authorized active-run Pokemon lookup command.
    /// </summary>
    public const string DebugPokemonLookup = "debug_pokemon_lookup";

    /// <summary>
    /// Gets the authorized active-run fusion preview command.
    /// </summary>
    public const string DebugFusionPreview = "debug_fusion_preview";
}
