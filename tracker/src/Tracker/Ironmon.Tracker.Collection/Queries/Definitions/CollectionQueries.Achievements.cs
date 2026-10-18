using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated access to generic evidence and opaque private checkpoints.
/// </summary>
internal static partial class CollectionQueries
{
    /// <summary>
    /// Declares compile-time achievement storage queries.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Reads the authenticated opaque consumer checkpoint without exposing its plaintext.
        /// </summary>
        /// <param name="key">The sixteen-byte opaque consumer identity.</param>
        /// <returns>The encrypted checkpoint, or null when this consumer has not been registered.</returns>
        public static SqlQuery<AchievementStateRecord?> ReadAchievementState(byte[] key)
        {
            return Sql.From<AchievementStateRow>().Where(row => row.Key == key).Select(row => new AchievementStateRecord { Key = row.Key, Binding = row.Binding, StateRevision = row.StateRevision, Through = row.Through, Format = row.Format, Slot = row.Slot, Nonce = row.Nonce, Tag = row.Tag, Ciphertext = row.Ciphertext }).FirstOrDefault();
        }

        /// <summary>
        /// Atomically replaces one encrypted checkpoint in the current collection transaction.
        /// </summary>
        /// <param name="key">The sixteen-byte opaque consumer identity.</param>
        /// <param name="binding">The thirty-two-byte authenticated definition-pack digest.</param>
        /// <param name="stateRevision">The monotonic private-checkpoint revision.</param>
        /// <param name="through">The eight-byte evaluated collection revision.</param>
        /// <param name="format">The checkpoint cipher format version.</param>
        /// <param name="slot">The encryption-key slot identifier.</param>
        /// <param name="nonce">The twelve-byte AES-GCM nonce.</param>
        /// <param name="tag">The sixteen-byte authentication tag.</param>
        /// <param name="ciphertext">The encrypted ledger and pending-notice identities.</param>
        /// <returns>The number of checkpoint rows inserted or updated.</returns>
        public static SqlQuery<int> SaveAchievementState(byte[] key, byte[] binding, long stateRevision, byte[] through, int format, int slot, byte[] nonce, byte[] tag, byte[] ciphertext)
        {
            return Sql.Insert(new AchievementStateRow { Key = key, Binding = binding, StateRevision = stateRevision, Through = through, Format = format, Slot = slot, Nonce = nonce, Tag = tag, Ciphertext = ciphertext }).OnConflict(row => row.Key).UpdateFromInserted(row => new { row.Binding, row.StateRevision, row.Through, row.Format, row.Slot, row.Nonce, row.Tag, row.Ciphertext }).ExecuteNonQuery();
        }

        /// <summary>
        /// Streams the complete jointly keyed typed dimensions for one generic group.
        /// </summary>
        /// <param name="group">The integer generic summary-group identity.</param>
        /// <returns>The matching typed rows, enumerated while the caller owns the connection and transaction.</returns>
        public static SqlQuery<IEnumerable<AchievementDimensionRecord>> FindAchievementDimensions(int group)
        {
            return Sql.From<DirectDimensionRefRow>().Join<DirectAtomLookupRow>((reference, atom) => reference.Atom == atom.Atom).Where((reference, atom) => reference.Group == group).Select((reference, atom) => new AchievementDimensionRecord { Series = reference.Series, Dimension = reference.Dimension, Value = atom.ValueId }).AsEnumerable();
        }

        /// <summary>
        /// Streams exact directly retained values for the selected generic evidence group.
        /// </summary>
        /// <param name="group">The integer generic summary-group identity.</param>
        /// <returns>The matching typed rows, enumerated while the caller owns the connection and transaction.</returns>
        public static SqlQuery<IEnumerable<AchievementScalarRecord>> FindAchievementScalars(int group)
        {
            return Sql.From<DirectScalarRow>().Where(row => row.Group == group).Select(row => new AchievementScalarRecord { Series = row.Series, Measure = row.Measure, Integer = row.Integer, Wide = row.Wide }).AsEnumerable();
        }

        /// <summary>
        /// Streams exact directly retained values for the selected generic evidence group.
        /// </summary>
        /// <param name="group">The integer generic summary-group identity.</param>
        /// <returns>The matching typed rows, enumerated while the caller owns the connection and transaction.</returns>
        public static SqlQuery<IEnumerable<AchievementSetRecord>> FindAchievementSets(int group)
        {
            return Sql.From<DirectSetMemberRow>().Where(row => row.Group == group).Select(row => new AchievementSetRecord { Series = row.Series, Measure = row.Measure, Member = row.Member }).AsEnumerable();
        }

        /// <summary>
        /// Streams exact directly retained values for the selected generic evidence group.
        /// </summary>
        /// <param name="group">The integer generic summary-group identity.</param>
        /// <returns>The matching typed rows, enumerated while the caller owns the connection and transaction.</returns>
        public static SqlQuery<IEnumerable<AchievementRatioRecord>> FindAchievementRatios(int group)
        {
            return Sql.From<DirectRatioRow>().Where(row => row.Group == group).Select(row => new AchievementRatioRecord { Series = row.Series, Measure = row.Measure, Numerator = row.Numerator, NumeratorWide = row.NumeratorWide, Denominator = row.Denominator, DenominatorWide = row.DenominatorWide }).AsEnumerable();
        }
    }
}
