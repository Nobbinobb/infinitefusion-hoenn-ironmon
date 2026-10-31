namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies startup stage events remain independent of a loaded save's run identity and counters.
/// </summary>
public sealed class StartupPreparationProgressPayloadTests
{
    private const string _id = "startup-codec";
    private const string _phase = "loading_save";
    private const string _idProperty = "preparation_id";
    private const string _statusProperty = "status";
    private const string _phaseProperty = "phase";

    /// <summary>
    /// Verifies the minimal stage-only wire payload round-trips with an empty run and initial envelope sequence.
    /// </summary>
    [Fact]
    public void StageOnlyEventRoundTripsWithoutLoadedRunIdentity()
    {
        StartupPreparationProgressPayload payload = new() { PreparationId = _id, Status = StartupPreparationProgressPayload.RunningStatus, Phase = _phase };
        TrackerMessage message = TrackerMessageFactory.CreateEvent(TrackerEvents.StartupPreparationProgress, 0, payload, string.Empty);
        string json = JsonSerializer.Serialize(message, TrackerJson.Options);
        TrackerMessage restored = JsonSerializer.Deserialize<TrackerMessage>(json, TrackerJson.Options)!;
        TrackerMessageValidator.Validate(restored);
        Assert.Equal(0, restored.Sequence);
        Assert.Equal(string.Empty, restored.RunId);
        Assert.Null(restored.BattleId);
        Assert.Equal([_idProperty, _statusProperty, _phaseProperty], restored.Payload.EnumerateObject().Select(property => property.Name));
        StartupPreparationProgressPayload decoded = TrackerJson.DeserializePayload<StartupPreparationProgressPayload>(restored.Payload);
        Assert.Equal(_id, decoded.PreparationId);
        Assert.Equal(StartupPreparationProgressPayload.RunningStatus, decoded.Status);
        Assert.Equal(_phase, decoded.Phase);
        Assert.Null(decoded.Error);
    }
}
