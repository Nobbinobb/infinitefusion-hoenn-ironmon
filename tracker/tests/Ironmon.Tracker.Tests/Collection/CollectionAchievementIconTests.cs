using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using System.Text;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies earned-only image access and the real notification wire payload.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const int _achievementDisplaySize = 50;
    private const string _iconDataPrefix = "data:image/png;base64,";
    private const string _unlocksKey = "unlocks";
    private const string _iconKey = "icon";
    private const string _pixelsKey = "pixels";
    private const string _conditionKey = "condition";
    private const string _previewTestVersion = "1.0";

    /// <summary>
    /// Withholds artwork for locked and unknown identities even when they are requested directly.
    /// </summary>
    /// <returns>A task completing after pre-unlock and post-unlock access are checked.</returns>
    [Fact]
    public async Task AchievementArtworkIsAvailableOnlyForEarnedIdentities()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        AchievementPack pack = new();
        Guid[] ids = [.. pack.Nodes.Select(node => node.Id)];
        Assert.Empty(await store.GetAchievementIconsAsync(ids));
        Assert.Empty(await store.GetAchievementIconsAsync([Guid.NewGuid()]));
        await store.AcceptBatchAsync(Batch(_stream, AchievementTrainer(_rivalClass, 1)));
        var earned = await store.GetAchievementsAsync();
        var icons = await store.GetAchievementIconsAsync(ids);
        Assert.Equal(earned.Select(item => item.Id).Order(), icons.Keys.Order());
        Assert.All(icons.Values, icon => Assert.Equal(_achievementDisplaySize * _achievementDisplaySize * 4, Convert.FromBase64String(icon.Pixels).Length));
    }

    /// <summary>
    /// Checks the shared fallback image without registering progress or writing image files.
    /// </summary>
    /// <returns>A task completing after the preview and durable ledger are inspected.</returns>
    [Fact]
    public async Task FallbackArtworkPreservesTheAchievementLedger()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        AchievementIcon icon = AchievementIconPack.Shared.Fallback;
        Assert.Equal(_achievementDisplaySize, icon.Width);
        Assert.Equal(_achievementDisplaySize, icon.Height);
        Assert.StartsWith(_iconDataPrefix, icon.ImageDataUri);
        byte[] png = Convert.FromBase64String(icon.ImageDataUri[_iconDataPrefix.Length..]);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        byte[] pixels = Convert.FromBase64String(icon.Pixels);
        Assert.Equal(icon.Width * icon.Height * 4, pixels.Length);
        Assert.Contains(Enumerable.Range(0, icon.Width * icon.Height), index => pixels[index * 4 + 3] == 0);
        Assert.Contains(Enumerable.Range(0, icon.Width * icon.Height), index => pixels[index * 4 + 3] > 200);
        Assert.Empty(await store.GetAchievementsAsync());
        Assert.Empty(await store.GetAchievementsAsync(true));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, "*.png", SearchOption.AllDirectories));
    }

    /// <summary>
    /// Sends bounded real earned notifications containing titles and pixels without unlock conditions.
    /// </summary>
    /// <returns>A task completing after production serialization and pending delivery state are checked.</returns>
    [Fact]
    public async Task AchievementWirePayloadContainsBoundedArtworkWithoutConditions()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        await store.AcceptBatchAsync(Batch(_stream, [.. TrainerWins(50, 1)]));
        TrackerDiagnosticsStore diagnostics = new();
        using TrackerRequestSession session = new(diagnostics);
        using MemoryStream bytes = new();
        await using TrackerMessageWriter writer = new(bytes, leaveOpen: true);
        session.Connect(writer);
        using GameplayCollectionReceiver receiver = new(store, session, diagnostics, true);
        TrackerMessage poll = TrackerMessageFactory.CreateEvent(GameplayCollectionProtocol.AchievementPoll, 1, new { });
        await receiver.ProcessAsync(poll, CancellationToken.None);
        string wire = Encoding.UTF8.GetString(bytes.ToArray()).Trim();
        Assert.InRange(wire.Length, 1, TrackerProtocol.MaximumMessageCharacters);
        TrackerMessage message = TrackerMessageCodec.Deserialize(wire);
        Assert.Equal(GameplayCollectionProtocol.AchievementNotifications, message.Event);
        var unlocks = message.Payload.GetProperty(_unlocksKey);
        Assert.InRange(unlocks.GetArrayLength(), 1, GameplayCollectionProtocol.MaximumAchievementNotices);
        foreach (var notice in unlocks.EnumerateArray())
        {
            Assert.False(notice.TryGetProperty(_conditionKey, out _));
            Assert.Equal(_achievementDisplaySize * _achievementDisplaySize * 4, notice.GetProperty(_iconKey).GetProperty(_pixelsKey).GetBytesFromBase64().Length);
        }

        Assert.All(diagnostics.Entries, entry => Assert.True(entry.RawValue.Length < 256));
        Assert.NotEmpty(await store.GetAchievementsAsync(true));
    }

}
