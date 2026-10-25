using System.Text.Json;
using Ironmon.Tracker.Collection.Queries.Definitions;
using Ironmon.Tracker.Collection.Queries.Results;
using Ironmon.Tracker.Collection.Storage.Encoding;

namespace Ironmon.Tracker.Collection.Storage;

/// <summary>
/// Retains compact closed-attempt counters for newly replayed gameplay without growing the active reducer checkpoint.
/// </summary>
internal sealed partial class CollectionDatabase
{

    /// <summary>
    /// Hydrates a continued attempt only when an actual new occurrence needs its previous sufficient counters.
    /// </summary>
    /// <param name="attempt">The actual attempt UUID in its 16-byte database representation.</param>
    private void RestoreContinuedAttempt(byte[] attempt)
    {
        string identity = CollectionEncoding.Identifier(attempt);
        if (_reducer.HasAttemptState(identity))
            return;

        CompletedAttemptRecord? stored = CollectionQueries.ReadCompletedAttemptState(_db, _transaction, attempt);
        if (stored is null)
            return;

        long length = stored.Length;
        byte[] state = CollectionEncoding.Inflate(stored.Payload, length);
        if (state.LongLength != length)
            throw new InvalidDataException("Archived attempt reducer checkpoint length is invalid.");

        using JsonDocument document = JsonDocument.Parse(state);
        _reducer.RestoreAttemptState(identity, document.RootElement);
    }

    /// <summary>
    /// Archives one completed attempt atomically with its receipt while removing it from frequently rewritten active state.
    /// </summary>
    /// <param name="attempt">The actual attempt UUID in its 16-byte database representation.</param>
    /// <param name="stream">The producer stream UUID in its 16-byte database representation.</param>
    /// <param name="sequence">The contiguous source sequence being committed with this evidence.</param>
    private void ArchiveCompletedAttempt(byte[] attempt, byte[] stream, ulong sequence)
    {
        JsonElement? state = _reducer.TakeCompletedAttemptState(CollectionEncoding.Identifier(attempt));
        if (state is not JsonElement actual)
            return;

        byte[] raw = CollectionEncoding.Canonical(actual);
        CollectionQueries.SaveCompletedAttemptState(_db, _transaction, attempt, CollectionEncoding.Compress(raw), raw.LongLength, stream, CollectionEncoding.Revision(sequence), CollectionEncoding.Revision(_revision));
    }
}
