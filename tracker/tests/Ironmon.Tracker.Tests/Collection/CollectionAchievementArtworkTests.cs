using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ironmon.Tracker.Tests.Collection;

/// <summary>
/// Verifies earned-only detail reads, fixed storage, and bounded authenticated resource access.
/// </summary>
public sealed partial class CollectionStoreTests
{
    private const string _artworkIdentity = "2915e809-e221-560b-a953-b8a4585002b0";
    private const string _artworkIndexResource = "Ironmon.Tracker.Collection.Achievements.AchievementArtwork.index.pack";
    private const string _artworkPngPrefix = "data:image/png;base64,";
    private const string _artworkFilePattern = "*";

    /// <summary>
    /// Keeps locked artwork private and repeated earned opens free of persistent image or database growth.
    /// </summary>
    /// <returns>A task completing after ledger, notification, thumbnail, and storage invariants are checked.</returns>
    [Fact]
    public async Task AchievementArtworkReadsAreEarnedOnlyAndDoNotGrowStorage()
    {
        using TestDirectory directory = new();
        await using GameplayCollectionStore store = new(directory.Path);
        Guid id = Guid.Parse(_artworkIdentity);
        Assert.Null(await store.GetAchievementArtworkAsync(id));
        Assert.Null(await store.GetAchievementArtworkAsync(Guid.NewGuid()));
        JsonElement consumption = JsonSerializer.SerializeToElement(new { item_id = _potion, disposition = _consumedDisposition, quantity = 1, battle_entry_owner = _player, current_controller = _player });
        await store.AcceptBatchAsync(Batch(_stream, [Occurrence(_resourceDisposal, consumption, 1)]));
        var earned = await store.GetAchievementsAsync();
        var pending = await store.GetAchievementsAsync(true);
        var thumbnails = await store.GetAchievementIconsAsync([id]);
        Assert.True(thumbnails[id].HasArtwork);
        Dictionary<string, long> filesBefore = ArtworkFileSizes(directory.Path);
        string? first = null;
        for (int open = 0; open < 8; open++)
        {
            AchievementArtwork artwork = Assert.IsType<AchievementArtwork>(await store.GetAchievementArtworkAsync(id));
            Assert.Equal(1024, artwork.Width);
            Assert.Equal(1024, artwork.Height);
            Assert.StartsWith(_artworkPngPrefix, artwork.ImageDataUri);
            byte[] png = Convert.FromBase64String(artwork.ImageDataUri[_artworkPngPrefix.Length..]);
            Assert.Equal(1024, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
            first ??= artwork.ImageDataUri;
            Assert.Equal(first, artwork.ImageDataUri);
        }

        Assert.Equal(earned, await store.GetAchievementsAsync());
        Assert.Equal(pending, await store.GetAchievementsAsync(true));
        var after = await store.GetAchievementIconsAsync([id]);
        Assert.Equal(thumbnails[id], after[id]);
        Assert.Equal(filesBefore.OrderBy(pair => pair.Key), ArtworkFileSizes(directory.Path).OrderBy(pair => pair.Key));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, _assignedImagePattern, SearchOption.AllDirectories));
    }

    /// <summary>
    /// Reads only the index until an image is selected and releases the read gate after cancellation.
    /// </summary>
    /// <returns>A task completing after lazy access, cancellation, and subsequent valid reads are checked.</returns>
    [Fact]
    public async Task AchievementArtworkIndexDoesNotLoadOrCacheTheGallery()
    {
        List<string> opened = [];
        AchievementArtworkPack pack = new(name =>
        {
            opened.Add(name);
            return typeof(GameplayCollectionStore).Assembly.GetManifestResourceStream(name);
        });
        Guid id = Guid.Parse(_artworkIdentity);
        Assert.True(pack.Contains(id));
        Assert.Equal([_artworkIndexResource], opened);
        Assert.Null(await pack.ForEarnedAsync(Guid.NewGuid(), CancellationToken.None));
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pack.ForEarnedAsync(id, cancelled.Token));
        Assert.Single(opened);
        AchievementArtwork first = Assert.IsType<AchievementArtwork>(await pack.ForEarnedAsync(id, CancellationToken.None));
        Assert.Equal(2, opened.Count);
        AchievementArtwork second = Assert.IsType<AchievementArtwork>(await pack.ForEarnedAsync(id, CancellationToken.None));
        Assert.Equal(3, opened.Count);
        Assert.Equal(first, second);
        Assert.NotSame(first, second);
    }

    /// <summary>
    /// Rejects inconsistent detail dimensions before any ciphertext part is opened.
    /// </summary>
    [Fact]
    public void AchievementArtworkRejectsMalformedIndexBeforeReadingImages()
    {
        byte[] index = ArtworkIndexBytes();
        BinaryPrimitives.WriteInt32LittleEndian(index.AsSpan(48, 4), 4096);
        Assert.Throws<InvalidDataException>(() => new AchievementArtworkPack(name => name == _artworkIndexResource ? new MemoryStream(index, false) : throw new InvalidOperationException("Image parts must remain unopened.")));
        Assert.Throws<InvalidDataException>(() => new AchievementArtworkPack(_ => new MemoryStream(index[..^1], false)));
    }

    /// <summary>
    /// Authenticates selected ciphertext rather than displaying a corrupted embedded image.
    /// </summary>
    /// <returns>A task completing after the tampered image is rejected.</returns>
    [Fact]
    public async Task AchievementArtworkRejectsTamperedCiphertext()
    {
        byte[] index = ArtworkIndexBytes();
        Guid firstId = new(index.AsSpan(16, 16));
        long offset = BinaryPrimitives.ReadInt64LittleEndian(index.AsSpan(36, 8));
        AchievementArtworkPack pack = new(name =>
        {
            if (name == _artworkIndexResource)
                return new MemoryStream(index, false);

            using Stream resource = typeof(GameplayCollectionStore).Assembly.GetManifestResourceStream(name)!;
            MemoryStream changed = new();
            resource.CopyTo(changed);
            changed.GetBuffer()[offset] ^= 1;
            changed.Position = 0;
            return changed;
        });
        await Assert.ThrowsAnyAsync<CryptographicException>(() => pack.ForEarnedAsync(firstId, CancellationToken.None));
    }

    /// <summary>
    /// Reads the production index for isolated corruption and bounds checks.
    /// </summary>
    /// <returns>A detached copy of the small opaque index.</returns>
    private static byte[] ArtworkIndexBytes()
    {
        using Stream stream = typeof(GameplayCollectionStore).Assembly.GetManifestResourceStream(_artworkIndexResource)!;
        using MemoryStream bytes = new();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    /// <summary>
    /// Snapshots profile file lengths without changing or checkpointing the collection database.
    /// </summary>
    /// <param name="directory">The test-owned profile directory.</param>
    /// <returns>The relative path and byte length of every profile file.</returns>
    private static Dictionary<string, long> ArtworkFileSizes(string directory)
        => Directory.EnumerateFiles(directory, _artworkFilePattern, SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path), path => new FileInfo(path).Length);
}
