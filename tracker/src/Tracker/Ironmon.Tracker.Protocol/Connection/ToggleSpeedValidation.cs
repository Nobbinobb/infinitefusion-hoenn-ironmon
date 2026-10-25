namespace Ironmon.Tracker.Protocol.Connection;

/// <summary>
/// Validates complete speed profiles before persistence or transport.
/// </summary>
public static class ToggleSpeedValidation
{
    /// <summary>
    /// Bounds stages per lane, including the fixed normal stage.
    /// </summary>
    public const int MaximumStagesPerLane = 128;

    /// <summary>
    /// Bounds portable stage, group, and connection identifiers.
    /// </summary>
    public const int MaximumIdentifierLength = 64;

    /// <summary>
    /// Validates cycles, entry connections, and coverage of every stage.
    /// </summary>
    /// <param name="configuration">The candidate saved profile.</param>
    /// <exception cref="ArgumentException">The profile is malformed or incomplete.</exception>
    public static void Validate(ToggleSpeedConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.Lanes is null || configuration.Links is null)
            throw new ArgumentException("The speed profile requires both lanes and connections.", nameof(configuration));

        HashSet<string> groups = new(StringComparer.Ordinal);
        HashSet<string> stages = new(StringComparer.Ordinal);
        Dictionary<string, (string Group, int Value)> overworld = ReadLane(configuration.Lanes.Overworld, groups, stages);
        Dictionary<string, (string Group, int Value)> battle = ReadLane(configuration.Lanes.Battle, groups, stages);
        HashSet<string> connected = new(StringComparer.Ordinal);
        HashSet<string> links = new(StringComparer.Ordinal);
        if (configuration.Links.Count > MaximumStagesPerLane - 1)
            throw new ArgumentException("The speed profile has too many connections.", nameof(configuration));

        foreach (ToggleSpeedLink link in configuration.Links)
        {
            if (link is null || !IsIdentifier(link.Id) || !links.Add(link.Id)
                || link.Overworld is null || !overworld.TryGetValue(link.Overworld, out var first)
                || link.Battle is null || !battle.TryGetValue(link.Battle, out var second)
                || first.Value == 1 || second.Value == 1)
            {
                throw new ArgumentException("Every connection must join valid accelerated stages in opposite lanes.", nameof(configuration));
            }

            if (!connected.Add(first.Group) || !connected.Add(second.Group))
                throw new ArgumentException("Each speed group can have only one connection.", nameof(configuration));
        }

        foreach (var (Group, Value) in overworld.Values.Concat(battle.Values))
        {
            if (Value != 1 && !connected.Contains(Group))
                throw new ArgumentException("Every multiplier or group needs a connection to the other lane before saving.", nameof(configuration));
        }
    }

    /// <summary>
    /// Validates one lane while building its stage-to-group index.
    /// </summary>
    /// <param name="lane">The ordered context-specific groups.</param>
    /// <param name="groups">The profile-wide group identifiers.</param>
    /// <param name="stages">The profile-wide stage identifiers.</param>
    /// <returns>Stage metadata for validating opposite-context connections.</returns>
    /// <exception cref="ArgumentException">A group, stage, or normal-speed boundary is invalid.</exception>
    private static Dictionary<string, (string Group, int Value)> ReadLane(IReadOnlyList<ToggleSpeedGroup> lane, HashSet<string> groups, HashSet<string> stages)
    {
        if (lane is null || lane.Count is 0 or > MaximumStagesPerLane)
            throw new ArgumentException("Each speed lane requires a bounded nonempty cycle.");

        Dictionary<string, (string Group, int Value)> result = new(StringComparer.Ordinal);
        for (int index = 0; index < lane.Count; index++)
        {
            ToggleSpeedGroup group = lane[index];
            if (group is null || !IsIdentifier(group.Id) || !groups.Add(group.Id) || group.Nodes is null || group.Nodes.Count == 0
                || group.Nodes.Count > MaximumStagesPerLane - result.Count)
            {
                throw new ArgumentException("Speed groups require unique identifiers and a bounded nonempty stage list.");
            }

            if (index == 0 && (group.Nodes.Count != 1 || group.Nodes[0] is null || group.Nodes[0].Value != 1))
                throw new ArgumentException("Normal speed must be the first standalone stage in both lanes.");

            foreach (ToggleSpeedNode node in group.Nodes)
            {
                if (node is null || !IsIdentifier(node.Id) || !stages.Add(node.Id) || (index == 0 ? node.Value != 1 : node.Value is < 2 or > 10))
                    throw new ArgumentException("Speed stages require unique identifiers and multipliers from 2 through 10 after normal speed.");

                result.Add(node.Id, (group.Id, node.Value));
            }
        }
        return result;
    }

    /// <summary>
    /// Checks a bounded identifier without admitting markup or selector syntax.
    /// </summary>
    /// <param name="value">The candidate identifier.</param>
    /// <returns>Whether the value is portable across Ruby, JSON, and DOM selectors.</returns>
    private static bool IsIdentifier(string value)
        => !string.IsNullOrEmpty(value) && value.Length <= MaximumIdentifierLength && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
