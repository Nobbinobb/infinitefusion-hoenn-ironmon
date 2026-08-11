namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Carries versioned authoritative statistics for one Ironmon attempt.
/// </summary>
public sealed class RunStatisticsPayload
{
    /// <summary>
    /// Initializes an empty run-statistics payload for protocol serialization.
    /// </summary>
    public RunStatisticsPayload()
    {
    }

    /// <summary>
    /// Gets or initializes the statistics schema version.
    /// </summary>
    public int SchemaVersion { get; init; }

    /// <summary>
    /// Gets or initializes the per-save attempt number.
    /// </summary>
    public int AttemptNumber { get; init; }

    /// <summary>
    /// Gets or initializes the deterministic run seed.
    /// </summary>
    public long Seed { get; init; }

    /// <summary>
    /// Gets or initializes the current or final result.
    /// </summary>
    public required string Result { get; init; }

    /// <summary>
    /// Gets or initializes accumulated active play time in seconds.
    /// </summary>
    public double ActiveSeconds { get; init; }

    /// <summary>
    /// Gets or initializes the number of attempts started in this save slot.
    /// </summary>
    public int AttemptsStarted { get; init; }

    /// <summary>
    /// Gets or initializes the number of lost attempts in this save slot.
    /// </summary>
    public int AttemptsLost { get; init; }

    /// <summary>
    /// Gets or initializes the number of won attempts in this save slot.
    /// </summary>
    public int AttemptsWon { get; init; }

    /// <summary>
    /// Gets or initializes the number of abandoned attempts in this save slot.
    /// </summary>
    public int AttemptsAbandoned { get; init; }

    /// <summary>
    /// Gets or initializes completed battles.
    /// </summary>
    public int BattlesCompleted { get; init; }

    /// <summary>
    /// Gets or initializes the highest usable player Pokemon level reached.
    /// </summary>
    public int HighestPlayerLevel { get; init; }

    /// <summary>
    /// Gets or initializes badges earned during the attempt.
    /// </summary>
    public int BadgesEarned { get; init; }

    /// <summary>
    /// Gets or initializes actual HP restored by consumed player items.
    /// </summary>
    public int TotalItemHealing { get; init; }

    /// <summary>
    /// Gets or initializes healing lost to the maximum-HP cap.
    /// </summary>
    public int WastedItemHealing { get; init; }

    /// <summary>
    /// Gets or initializes all consumed player resources.
    /// </summary>
    public int ItemsUsed { get; init; }

    /// <summary>
    /// Gets or initializes item counts grouped by Bag or Held source and item ID.
    /// </summary>
    public IReadOnlyDictionary<string, Dictionary<string, int>> ItemsBySource { get; init; } = new Dictionary<string, Dictionary<string, int>>();

    /// <summary>
    /// Gets or initializes displayed trainer-species encounter frequencies.
    /// </summary>
    public IReadOnlyDictionary<string, int> TrainerSpeciesCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>
    /// Gets or initializes the number of distinct displayed trainer species encountered.
    /// </summary>
    public int TrainerSpeciesDistinct { get; init; }

    /// <summary>
    /// Gets or initializes every trainer species tied for most encounters.
    /// </summary>
    public IReadOnlyList<string> TrainerSpeciesMostEncountered { get; init; } = [];

    /// <summary>
    /// Gets or initializes the number of trainer Pokemon defeated.
    /// </summary>
    public int TrainerDefeatedCount { get; init; }

    /// <summary>
    /// Gets or initializes the average generated BST of defeated trainer Pokemon.
    /// </summary>
    public double? TrainerDefeatedBstAverage { get; init; }

    /// <summary>
    /// Gets or initializes the minimum generated BST among defeated trainer Pokemon.
    /// </summary>
    public int? TrainerDefeatedBstMinimum { get; init; }

    /// <summary>
    /// Gets or initializes every species tied at the minimum generated BST.
    /// </summary>
    public IReadOnlyList<string> TrainerDefeatedBstMinimumSpecies { get; init; } = [];

    /// <summary>
    /// Gets or initializes the maximum generated BST among defeated trainer Pokemon.
    /// </summary>
    public int? TrainerDefeatedBstMaximum { get; init; }

    /// <summary>
    /// Gets or initializes every species tied at the maximum generated BST.
    /// </summary>
    public IReadOnlyList<string> TrainerDefeatedBstMaximumSpecies { get; init; } = [];
}
