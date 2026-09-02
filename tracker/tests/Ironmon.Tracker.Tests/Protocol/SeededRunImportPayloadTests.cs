namespace Ironmon.Tracker.Tests.Protocol;

/// <summary>
/// Verifies the transactional seeded-run import protocol contract.
/// </summary>
public sealed class SeededRunImportPayloadTests
{
    /// <summary>
    /// Verifies normalized token inputs and lifecycle statuses use canonical protocol JSON.
    /// </summary>
    [Fact]
    public void ImportPayloadsUseCanonicalJson()
    {
        SeededRunImportRequestPayload request = new()
        {
            TokenId = "seed-token-42",
            Seed = 42,
            GameVersion = "6.8.0",
            IronmonVersion = "0.7.7",
            DataMode = "classic",
            Configuration = new RunConfigurationPayload
            {
                SchemaVersion = 1,
                WildPolicy = "mixed",
                TrainerPolicy = "normal_only",
                UnfusionSetting = "player_choice",
                AutomaticReset = false
            },
            CompatibilityFingerprint = new string('a', 64)
        };

        SeededRunImportStatusPayload status = new()
        {
            TokenId = request.TokenId,
            Status = SeededRunImportStatus.Queued,
            Message = "Waiting for a safe map boundary."
        };

        string requestJson = TrackerJson.SerializePayload(request).GetRawText();
        string statusJson = TrackerJson.SerializePayload(status).GetRawText();

        Assert.Contains("\"compatibility_fingerprint\":", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"automatic_reset\":false", requestJson, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"queued\"", statusJson, StringComparison.Ordinal);
        Assert.Equal(request.Seed, TrackerJson.DeserializePayload<SeededRunImportRequestPayload>(TrackerJson.SerializePayload(request)).Seed);
    }
}
