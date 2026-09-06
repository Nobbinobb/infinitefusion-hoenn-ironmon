namespace Ironmon.Tracker.Protocol.Live;

/// <summary>
/// Describes the complete legal tracker view of the current starter selection.
/// </summary>
public sealed class StarterSelectionSnapshot
{
    /// <summary>
    /// Initializes an empty starter-selection snapshot for protocol serialization.
    /// </summary>
    public StarterSelectionSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes whether starter selection is currently active.
    /// </summary>
    public bool Active { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based random starter pick.
    /// </summary>
    public int? RandomPickIndex { get; init; }

    /// <summary>
    /// Gets or initializes the inclusive starter-selection BST ceiling when enabled.
    /// </summary>
    public int? MaximumBaseStatTotal { get; init; }

    /// <summary>
    /// Gets or initializes the identifier binding requests to this opening of the starter screen.
    /// </summary>
    public string? SelectionId { get; init; }

    /// <summary>
    /// Gets or initializes whether the game is using automatic selection rules.
    /// </summary>
    public bool AutoSelect { get; init; }

    /// <summary>
    /// Gets or initializes the starter slots in display order.
    /// </summary>
    public IReadOnlyList<StarterChoiceSnapshot> Choices { get; init; } = [];
}
