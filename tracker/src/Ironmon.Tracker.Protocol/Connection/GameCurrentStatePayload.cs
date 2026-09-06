namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes the connection-level game state returned during recovery.
/// </summary>
public sealed class GameCurrentStatePayload
{
    /// <summary>
    /// Initializes a connection-level game state payload.
    /// </summary>
    /// <param name="ironmonActive">Whether Ironmon mode is active.</param>
    /// <param name="runId">The current run identifier.</param>
    /// <param name="battleId">The current battle identifier.</param>
    /// <param name="sequence">The latest game event sequence.</param>
    /// <param name="battle">The active battle when one exists.</param>
    /// <param name="player">The initialized player Pokemon when one exists.</param>
    /// <param name="enemies">The legally visible active opposing Pokemon.</param>
    /// <param name="starterSelection">The active starter-selection view when one exists.</param>
    /// <param name="attemptStatistics">The current attempt statistics when available.</param>
    /// <param name="completedRun">The completed-run recipe when the active save's run has ended.</param>
    /// <param name="typeCoverage">The non-sensitive type-coverage compatibility context when supported.</param>
    /// <param name="fusionAssignments">The authorized active-run recipe for early native fusion assignment preparation.</param>
    /// <param name="activeRunPreparationReady">Whether the active run has reached the safe background-preparation boundary.</param>
    /// <param name="overworldEncounters">The current overworld encounter option, or null for older games.</param>
    /// <param name="badges">The eight individual gym badge flags in trainer-card order, or null for older games.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when sequence is negative.</exception>
    public GameCurrentStatePayload(bool ironmonActive, string? runId, string? battleId, long sequence, BattleSnapshot? battle = null, PlayerPokemonSnapshot? player = null, IReadOnlyList<EnemyPokemonSnapshot>? enemies = null, StarterSelectionSnapshot? starterSelection = null, RunStatisticsPayload? attemptStatistics = null, CompletedRunRecipePayload? completedRun = null, TypeCoverageContextPayload? typeCoverage = null, FusionAssignmentRecipePayload? fusionAssignments = null, bool activeRunPreparationReady = false, bool? overworldEncounters = null, IReadOnlyList<bool>? badges = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        IronmonActive = ironmonActive;
        RunId = runId;
        BattleId = battleId;
        Sequence = sequence;
        Battle = battle;
        Player = player;
        Enemies = enemies ?? [];
        StarterSelection = starterSelection;
        AttemptStatistics = attemptStatistics;
        CompletedRun = completedRun;
        TypeCoverage = typeCoverage;
        FusionAssignments = fusionAssignments;
        ActiveRunPreparationReady = activeRunPreparationReady;
        OverworldEncounters = overworldEncounters;
        Badges = badges;
    }

    /// <summary>
    /// Gets whether Ironmon mode is active.
    /// </summary>
    public bool IronmonActive { get; }

    /// <summary>
    /// Gets the current run identifier when one exists.
    /// </summary>
    public string? RunId { get; }

    /// <summary>
    /// Gets the current battle identifier when one exists.
    /// </summary>
    public string? BattleId { get; }

    /// <summary>
    /// Gets the latest game event sequence.
    /// </summary>
    public long Sequence { get; }

    /// <summary>
    /// Gets the active battle when one exists.
    /// </summary>
    public BattleSnapshot? Battle { get; }

    /// <summary>
    /// Gets the most recently initialized player Pokemon when one exists.
    /// </summary>
    public PlayerPokemonSnapshot? Player { get; }

    /// <summary>
    /// Gets the legally visible active opposing Pokemon.
    /// </summary>
    public IReadOnlyList<EnemyPokemonSnapshot> Enemies { get; }

    /// <summary>
    /// Gets the active starter-selection view when one exists.
    /// </summary>
    public StarterSelectionSnapshot? StarterSelection { get; }

    /// <summary>
    /// Gets the current attempt statistics when available.
    /// </summary>
    public RunStatisticsPayload? AttemptStatistics { get; }

    /// <summary>
    /// Gets the completed-run recipe when the active save's run has ended.
    /// </summary>
    public CompletedRunRecipePayload? CompletedRun { get; }

    /// <summary>
    /// Gets the non-sensitive type-coverage compatibility context when supported.
    /// </summary>
    public TypeCoverageContextPayload? TypeCoverage { get; }

    /// <summary>
    /// Gets the authorized active-run recipe used to prepare exact fusion assignments before graph or material requests.
    /// </summary>
    public FusionAssignmentRecipePayload? FusionAssignments { get; }

    /// <summary>
    /// Gets whether the active run has reached the safe background-preparation boundary.
    /// </summary>
    public bool ActiveRunPreparationReady { get; }

    /// <summary>
    /// Gets the current overworld encounter option, or null when not reported by the game.
    /// </summary>
    public bool? OverworldEncounters { get; }

    /// <summary>
    /// Gets the individual gym badge flags in trainer-card order, or null when unavailable.
    /// </summary>
    public IReadOnlyList<bool>? Badges { get; }
}
