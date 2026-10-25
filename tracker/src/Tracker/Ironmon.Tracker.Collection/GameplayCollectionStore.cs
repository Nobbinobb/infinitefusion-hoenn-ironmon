using System.Text.Json;
using System.Threading.Channels;
using Ironmon.Tracker.Collection.Achievements;
using Ironmon.Tracker.Collection.Storage;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Owns one serialized, durable gameplay collection writer independently of achievement processing.
/// </summary>
public sealed partial class GameplayCollectionStore : IAsyncDisposable
{
    private static readonly Lazy<int> _achievementCount = new(() => new AchievementPack().Nodes.Count());
    private readonly Channel<CollectionWork> _queue = Channel.CreateBounded<CollectionWork>(new BoundedChannelOptions(32) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _worker;
    private readonly string _profileDirectory;
    private readonly string? _historyRunDirectory;
    private const string _historyEventsProperty = "events";
    private const string _historyTypeProperty = "type";
    private const string _historyAttemptProperty = "attempt_id";
    private const string _historyOwnedEvent = "pokemon.owned_configuration_committed";

    /// <summary>
    /// Reports exact attempts whose ownership records were durably received, including delayed terminal batches.
    /// </summary>
    public event Action<IReadOnlyList<string>>? OwnedHistoryChanged;

    /// <summary>
    /// Creates a collection worker for an external profile directory.
    /// </summary>
    /// <param name="profileDirectory">The directory containing the collection database, separate from game saves.</param>
    /// <param name="runDirectory">Optional tracker-owned completed-run recipe directory included in history maintenance.</param>
    public GameplayCollectionStore(string profileDirectory, string? runDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        _profileDirectory = Path.GetFullPath(profileDirectory);
        _historyRunDirectory = runDirectory is null ? null : Path.GetFullPath(runDirectory);
        _worker = Task.Run(RunWriterAsync);
    }

    /// <summary>
    /// Gets the latest operational error without exposing collected gameplay payloads.
    /// </summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// Gets the current pack's total without decrypting locked achievement definitions.
    /// </summary>
    public static int AchievementCount => _achievementCount.Value;

    /// <summary>
    /// Applies one ordered batch and returns its durable acknowledgement.
    /// </summary>
    /// <param name="payload">An immutable versioned collection batch.</param>
    /// <param name="cancellationToken">Cancels the caller's wait, never a committed collection transaction.</param>
    /// <returns>The matching store binding and durable contiguous receipt prefix.</returns>
    public async Task<JsonElement> AcceptBatchAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        JsonElement copy = payload.Clone();
        JsonElement acknowledgement = await EnqueueAsync(database => database.Accept(copy), cancellationToken, true).ConfigureAwait(false);
        if (OwnedHistoryChanged is not null)
        {
            string[] attempts = [.. copy.GetProperty(_historyEventsProperty).EnumerateArray()
                .Where(occurrence => occurrence.GetProperty(_historyTypeProperty).GetString() == _historyOwnedEvent && occurrence.TryGetProperty(_historyAttemptProperty, out JsonElement attempt) && attempt.ValueKind == JsonValueKind.String)
                .Select(occurrence => occurrence.GetProperty(_historyAttemptProperty).GetString()!).Distinct(StringComparer.OrdinalIgnoreCase)];

            if (attempts.Length > 0)
                NotifyOwnedHistoryChanged(attempts);
        }

        return acknowledgement;
    }

    /// <summary>
    /// Delivers history refresh notices without allowing a view failure to invalidate a durable receipt.
    /// </summary>
    /// <param name="attempts">The exact attempts affected by the committed batch.</param>
    private void NotifyOwnedHistoryChanged(IReadOnlyList<string> attempts)
    {
        Delegate[] handlers = OwnedHistoryChanged?.GetInvocationList() ?? [];
        foreach (Action<IReadOnlyList<string>> handler in handlers)
        {
            try
            {
                handler(attempts);
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Reads operational storage and timing measurements without exporting gameplay records.
    /// </summary>
    /// <param name="cancellationToken">Cancels the caller's wait.</param>
    /// <returns>Actual database/WAL bytes, table row counts and writer timing totals.</returns>
    public Task<JsonElement> GetMeasurementsAsync(CancellationToken cancellationToken = default)
        => EnqueueAsync(database => database.Measurements(), cancellationToken);

    /// <summary>
    /// Reads the latest retained owned configuration for every recorded individual, species and form.
    /// </summary>
    /// <param name="attemptIds">Exact compact UUIDs associated with the selected run.</param>
    /// <param name="cancellationToken">Cancels admission or waiting without interrupting collection writes.</param>
    /// <returns>Recorded configurations from the requested attempts, excluding encounter-only observations.</returns>
    public async Task<IReadOnlyList<JsonElement>> GetOwnedConfigurationsAsync(IReadOnlyList<string> attemptIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attemptIds);
        byte[][] attempts = [.. attemptIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(CollectionEncoding.Identifier)];
        if (attempts.Length == 0)
            return [];

        JsonElement snapshot = await EnqueueAsync(database => database.OwnedLookup(attempts), cancellationToken).ConfigureAwait(false);
        return [.. snapshot.EnumerateArray()];
    }

    /// <summary>
    /// Reads newest-first retained runner stages and peak levels for an archived run.
    /// </summary>
    /// <param name="attemptIds">Exact compact UUIDs explicitly associated with the selected run.</param>
    /// <param name="cancellationToken">Cancels the read without interrupting collection writes.</param>
    /// <returns>Recorded stages, empty when the run has no retained ownership evidence.</returns>
    public async Task<IReadOnlyList<RunPokemonHistoryEntry>> GetRunPokemonHistoryAsync(IReadOnlyList<string> attemptIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attemptIds);
        byte[][] attempts = [.. attemptIds.Distinct(StringComparer.OrdinalIgnoreCase).Select(CollectionEncoding.Identifier)];
        if (attempts.Length == 0)
            return [];

        JsonElement snapshot = await EnqueueAsync(database => database.RunPokemonHistory(attempts), cancellationToken).ConfigureAwait(false);
        return snapshot.Deserialize<RunPokemonHistoryEntry[]>() ?? [];
    }

    /// <summary>
    /// Reads only committed earned achievements, including while the game is disconnected.
    /// </summary>
    /// <param name="pendingOnly">Whether to return the durable unacknowledged game-notification subset.</param>
    /// <param name="cancellationToken">Cancels the caller's queued wait without cancelling a committed unlock.</param>
    /// <returns>The revealed earned list; no locked names, conditions or progress are returned.</returns>
    public async Task<IReadOnlyList<UnlockedAchievement>> GetAchievementsAsync(bool pendingOnly = false, CancellationToken cancellationToken = default)
    {
        JsonElement snapshot = await EnqueueAsync(database => database.AchievementSnapshot(pendingOnly), cancellationToken).ConfigureAwait(false);
        return snapshot.Deserialize<UnlockedAchievement[]>() ?? [];
    }

    /// <summary>
    /// Durably acknowledges game notifications after their actual display while preserving all unlocks.
    /// </summary>
    /// <param name="ids">The stable earned identities actually shown by the game.</param>
    /// <param name="cancellationToken">Cancels queue admission or waiting, never a committed acknowledgement.</param>
    /// <returns>A task completing after the encrypted pending queue has committed.</returns>
    public async Task AcknowledgeAchievementsAsync(IReadOnlySet<Guid> ids, CancellationToken cancellationToken = default)
    {
        await EnqueueAsync(database => database.AcknowledgeAchievements(ids), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads only future-unlock wardrobe rewards waiting for durable game credit.
    /// </summary>
    /// <param name="cancellationToken">Cancels the caller's wait.</param>
    /// <returns>The pending earned identities, without retroactive existing unlocks.</returns>
    public async Task<IReadOnlyList<Guid>> GetWardrobeRewardsAsync(CancellationToken cancellationToken = default)
    {
        JsonElement snapshot = await EnqueueAsync(database => database.WardrobeRewardsSnapshot(), cancellationToken).ConfigureAwait(false);
        return snapshot.Deserialize<Guid[]>() ?? [];
    }

    /// <summary>
    /// Completes reward delivery only after the game persists its corresponding currency claims.
    /// </summary>
    /// <param name="ids">The identities credited by the game.</param>
    /// <param name="cancellationToken">Cancels admission or waiting, never a committed acknowledgement.</param>
    /// <returns>Completion after the reward acknowledgement is durable.</returns>
    public async Task AcknowledgeWardrobeRewardsAsync(IReadOnlySet<Guid> ids, CancellationToken cancellationToken = default)
    {
        await EnqueueAsync(database => database.AcknowledgeWardrobeRewards(ids), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Drains accepted work, checkpoints the database and releases profile ownership.
    /// </summary>
    /// <returns>A value task that completes after accepted work drains, SQLite checkpoints and exclusive profile ownership is released.</returns>
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }

    /// <summary>
    /// Queues database work with finite backpressure outside the game thread.
    /// </summary>
    /// <param name="operation">The operation to run on the sole database writer.</param>
    /// <param name="cancellationToken">Cancels queue admission or the caller's wait without undoing a committed transaction.</param>
    /// <param name="clearsError">Whether successful completion clears the latest operational collection error.</param>
    /// <returns>A task whose result is the operation's JSON response after the sole writer completes it.</returns>
    private async Task<JsonElement> EnqueueAsync(Func<CollectionDatabase, JsonElement> operation, CancellationToken cancellationToken, bool clearsError = false)
    {
        TaskCompletionSource<JsonElement> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await _queue.Writer.WriteAsync(new CollectionWork(operation, completion, clearsError), cancellationToken).ConfigureAwait(false);
        return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs synchronous SQLite calls on the sole worker rather than a UI or socket callback.
    /// </summary>
    /// <returns>A task that completes when the queue drains and profile ownership is released, including startup-failure cleanup.</returns>
    private async Task RunWriterAsync()
    {
        try
        {
            using CollectionDatabase database = new(_profileDirectory);
            await foreach (CollectionWork work in _queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try
                {
                    work.Completion.SetResult(work.Operation(database));
                    if (work.ClearsError)
                        LastError = null;
                }
                catch (Exception error)
                {
                    LastError = error.Message;
                    work.Completion.SetException(error);
                }
            }
        }
        catch (Exception error)
        {
            LastError = error.Message;
            _queue.Writer.TryComplete(error);
            while (_queue.Reader.TryRead(out CollectionWork? pending))
                pending.Completion.TrySetException(error);
        }
    }

    /// <summary>
    /// Holds one worker request and its asynchronous response.
    /// </summary>
    /// <remarks>
    /// Initializes the request from its operation and completion.
    /// </remarks>
    /// <param name="Operation">The sole-worker operation.</param>
    /// <param name="Completion">The caller's completion signal.</param>
    /// <param name="ClearsError">Whether a successful durable write resolves the latest operational error.</param>
    private sealed record CollectionWork(Func<CollectionDatabase, JsonElement> Operation, TaskCompletionSource<JsonElement> Completion, bool ClearsError);
}
