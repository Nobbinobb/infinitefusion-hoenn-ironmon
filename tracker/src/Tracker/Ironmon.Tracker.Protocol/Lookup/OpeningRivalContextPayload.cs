namespace Ironmon.Tracker.Protocol.Lookup;

/// <summary>
/// Retains cheap deterministic story inputs when an opening loss must reset before preparing future teams.
/// </summary>
public sealed class OpeningRivalContextPayload
{
    /// <summary>
    /// Gets or initializes the context contract version.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// Gets or initializes the zero-based selected starter slot.
    /// </summary>
    public int ChosenStarterIndex { get; init; }

    /// <summary>
    /// Gets or initializes the authored trainer gender variable used by story reconstruction.
    /// </summary>
    public int TrainerGender { get; init; }

    /// <summary>
    /// Gets or initializes the rival name used in deterministic story party mapping.
    /// </summary>
    public string RivalName { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the saved difficulty selection.
    /// </summary>
    public int SelectedDifficulty { get; init; }

    /// <summary>
    /// Gets or initializes the saved lowest difficulty.
    /// </summary>
    public int LowestDifficulty { get; init; }

    /// <summary>
    /// Gets or initializes all three offered species in their original slot order.
    /// </summary>
    public IReadOnlyList<string> StarterSpeciesIds { get; init; } = [];

    /// <summary>
    /// Gets or initializes the post-lab species when already known without preparing a fusion assignment.
    /// </summary>
    public string? LabSpeciesId { get; init; }
}
