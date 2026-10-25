namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies complete cycle mappings, duplicate positions, and transport compatibility.
/// </summary>
public sealed class ToggleSpeedTests
{
    private const string Overworld = "overworld";
    private const string Battle = "battle";
    private const string Lanes = "lanes";
    private const string Nodes = "nodes";
    private const string Value = "value";
    private const string Id = "id";
    private const string Links = "links";
    private const string DuplicateId = "another-two";

    /// <summary>
    /// Preserves exact lane order and distinct positions with equal values through settings and handshake JSON.
    /// </summary>
    [Fact]
    public void DuplicatePositionsRoundTripWithTheirGroupsAndEntries()
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(ToggleSpeedConfiguration.CreateDefault(), TrackerJson.Options))!;
        var members = json[Lanes]![Overworld]![1]![Nodes]!.AsArray();
        members.Add(new System.Text.Json.Nodes.JsonObject { [Id] = DuplicateId, [Value] = 2 });
        members.Add(new System.Text.Json.Nodes.JsonObject { [Id] = DuplicateId + DuplicateId, [Value] = 10 });
        ToggleSpeedConfiguration profile = json.Deserialize<ToggleSpeedConfiguration>(TrackerJson.Options)!;
        ToggleSpeedValidation.Validate(profile);
        TrackerSettingsPayload restored = TrackerJson.DeserializePayload<TrackerSettingsPayload>(TrackerJson.SerializePayload(new TrackerSettingsPayload { ToggleSpeed = profile }));
        Assert.Equal([1, 2, 2, 10, 3], restored.ToggleSpeed!.Lanes.Overworld.SelectMany(group => group.Nodes).Select(node => node.Value));
        Assert.Equal(DuplicateId, restored.ToggleSpeed.Lanes.Overworld[1].Nodes[1].Id);
        TrackerHandshakePayload handshake = new("test", false, false) { ToggleSpeed = profile };
        ToggleSpeedValidation.Validate(TrackerJson.DeserializePayload<TrackerHandshakePayload>(TrackerJson.SerializePayload(handshake)).ToggleSpeed);
        Assert.Null(new TrackerSettingsPayload().ToggleSpeed);
    }

    /// <summary>
    /// Rejects incomplete coverage, duplicate connections, malformed identifiers, and invalid normal or accelerated stages.
    /// </summary>
    /// <param name="variant">The malformed profile variant.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void RejectsProfilesWithoutATotalUnambiguousMapping(int variant)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(ToggleSpeedConfiguration.CreateDefault(), TrackerJson.Options))!;
        if (variant == 0)
            json[Links]!.AsArray().RemoveAt(0);
        if (variant == 1)
            json[Links]![1]![Overworld] = json[Links]![0]![Overworld]!.GetValue<string>();
        if (variant == 2)
            json[Lanes]![Battle]![1]![Nodes]![0]![Value] = 11;
        if (variant == 3)
            json[Lanes]![Overworld]![1]![Nodes]![0]![Value] = 1;
        if (variant == 4)
            json[Lanes]![Battle]![0]![Nodes]![0]![Value] = 2;
        if (variant == 5)
            json[Lanes]![Overworld]![1]![Nodes]![0]![Id] = new string('a', ToggleSpeedValidation.MaximumIdentifierLength + 1);
        if (variant == 6)
            json[Lanes]![Battle]![1]![Nodes]![0]![Id] = json[Lanes]![Overworld]![1]![Nodes]![0]![Id]!.GetValue<string>();
        if (variant == 7)
            json[Links]![0]![Overworld] = json[Lanes]![Overworld]![0]![Nodes]![0]![Id]!.GetValue<string>();
        if (variant == 8)
            json[Links]![1]![Id] = json[Links]![0]![Id]!.GetValue<string>();
        Assert.Throws<ArgumentException>(() => ToggleSpeedValidation.Validate(json.Deserialize<ToggleSpeedConfiguration>(TrackerJson.Options)!));
    }

    /// <summary>
    /// Accepts the fixed normal cycle alone and checks that defaults are independent objects.
    /// </summary>
    [Fact]
    public void NormalOnlyCyclesNeedNoExplicitConnection()
    {
        ToggleSpeedConfiguration first = ToggleSpeedConfiguration.CreateDefault();
        ToggleSpeedConfiguration normal = new() { Lanes = new() { Overworld = [first.Lanes.Overworld[0]], Battle = [first.Lanes.Battle[0]] } };
        ToggleSpeedValidation.Validate(normal);
        Assert.NotSame(first.Lanes.Overworld, ToggleSpeedConfiguration.CreateDefault().Lanes.Overworld);
        Assert.Equal([1, 2, 3], first.Lanes.Battle.SelectMany(group => group.Nodes).Select(node => node.Value));
    }
}
