namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Describes ordered speed groups and their bidirectional entry connections.
/// </summary>
public sealed class ToggleSpeedConfiguration
{
    /// <summary>
    /// Gets or initializes the two context-specific cycles.
    /// </summary>
    public ToggleSpeedLanes Lanes { get; init; } = new();

    /// <summary>
    /// Gets or initializes the entry-point connections between groups.
    /// </summary>
    public IReadOnlyList<ToggleSpeedLink> Links { get; init; } = [];

    /// <summary>
    /// Creates independent default cycles containing normal, double, and triple speed.
    /// </summary>
    /// <returns>A complete configuration with matching double and triple entries.</returns>
    public static ToggleSpeedConfiguration CreateDefault() => new()
    {
        Lanes = new() { Overworld = CreateLane(OverworldPrefix), Battle = CreateLane(BattlePrefix) },
        Links = [.. Enumerable.Range(2, 2).Select(value => new ToggleSpeedLink { Id = LinkPrefix + value, Overworld = OverworldPrefix + NodeSeparator + value, Battle = BattlePrefix + NodeSeparator + value })]
    };

    private const string OverworldPrefix = "ow";
    private const string BattlePrefix = "bt";
    private const string GroupSeparator = "-group-";
    private const string NodeSeparator = "-";
    private const string LinkPrefix = "matching-";

    /// <summary>
    /// Creates an independent normal, double, triple cycle.
    /// </summary>
    /// <param name="prefix">The context-specific identifier prefix.</param>
    /// <returns>The default standalone groups in toggle order.</returns>
    private static IReadOnlyList<ToggleSpeedGroup> CreateLane(string prefix)
        => [.. Enumerable.Range(1, 3).Select(value => Group(prefix + GroupSeparator + value, prefix + NodeSeparator + value, value))];

    /// <summary>
    /// Creates a standalone stage for a default cycle.
    /// </summary>
    /// <param name="group">The stable group identifier.</param>
    /// <param name="node">The stable stage identifier.</param>
    /// <param name="value">The target multiplier.</param>
    /// <returns>A group containing one stage.</returns>
    private static ToggleSpeedGroup Group(string group, string node, int value)
        => new() { Id = group, Nodes = [new() { Id = node, Value = value }] };
}

/// <summary>
/// Contains the ordered overworld and battle group lists.
/// </summary>
public sealed class ToggleSpeedLanes
{
    /// <summary>
    /// Gets or initializes the overworld groups in toggle order.
    /// </summary>
    public IReadOnlyList<ToggleSpeedGroup> Overworld { get; init; } = [];

    /// <summary>
    /// Gets or initializes the battle groups in toggle order.
    /// </summary>
    public IReadOnlyList<ToggleSpeedGroup> Battle { get; init; } = [];
}

/// <summary>
/// Groups stages that share one opposite-context connection and remembered selection.
/// </summary>
public sealed class ToggleSpeedGroup
{
    /// <summary>
    /// Gets or initializes the stable identifier used for group memory.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the member stages in toggle order.
    /// </summary>
    public IReadOnlyList<ToggleSpeedNode> Nodes { get; init; } = [];
}

/// <summary>
/// Identifies one cycle position independently of its multiplier.
/// </summary>
public sealed class ToggleSpeedNode
{
    /// <summary>
    /// Gets or initializes the stable stage identifier, including for duplicate values.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the target multiplier.
    /// </summary>
    public int Value { get; init; }
}

/// <summary>
/// Selects the first-visit entry stage at each end of a group connection.
/// </summary>
public sealed class ToggleSpeedLink
{
    /// <summary>
    /// Gets or initializes the stable wire identifier.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the overworld entry stage identifier.
    /// </summary>
    public string Overworld { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the battle entry stage identifier.
    /// </summary>
    public string Battle { get; init; } = string.Empty;
}
