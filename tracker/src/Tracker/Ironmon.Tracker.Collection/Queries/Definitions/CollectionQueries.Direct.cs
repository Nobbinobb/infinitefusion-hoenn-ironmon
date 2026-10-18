using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated direct storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _signedInteger = "signed_integer";
    private const string _nativeMember = "native";
    private const string _sourceFactWitnessKind = "source_fact";

    /// <summary>
    /// Declares typed direct queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Upserts a direct scalar while retaining its original group and value domain on conflict.
        /// </summary>
        /// <param name="series">The exact directly indexed series identity.</param>
        /// <param name="group">The existing owning summary group.</param>
        /// <param name="measure">The measure ordinal within the group.</param>
        /// <param name="integer">The signed 64-bit value, or null when its decimal overflow is used.</param>
        /// <param name="wide">The exact decimal overflow text, or null when the native integer is used.</param>
        /// <param name="witness">The retained witness identity, or null for an ordinary cumulative scalar.</param>
        /// <returns>The provider's affected-row count after the insertion or conflict update.</returns>
        public static SqlQuery<int> SaveScalar(long series, int group, int measure, long? integer, string? wide, long? witness)
        {
            return Sql.Insert(new DirectScalarRow { Series = series, Group = group, Measure = measure, Domain = _signedInteger, Integer = integer, Wide = wide, Witness = witness })
                .OnConflict(row => new { row.Series, row.Measure })
                .UpdateFromInserted(row => new { row.Integer, row.Wide, row.Witness })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves an existing atom by its exact immutable dictionary identity.
        /// </summary>
        /// <param name="valueId">The immutable dictionary value identity.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<DirectAtomRecord?> FindAtom(long valueId)
        {
            return Sql.From<DirectAtomLookupRow>()
                .Where(row => row.ValueId == valueId)
                .Select(row => new DirectAtomRecord { Atom = row.Atom })
                .FirstOrDefault();
        }

        /// <summary>
        /// Adds an exact member of an unreferenced set atom with native duplicate suppression.
        /// </summary>
        /// <param name="atom">The interned exact dimension atom identity.</param>
        /// <param name="member">The immutable dictionary member identity.</param>
        /// <returns>The affected-row count; ignored duplicate members return zero.</returns>
        public static SqlQuery<int> AddAtomMember(long atom, long member)
        {
            return Sql.Insert(new DirectAtomMemberRow { Atom = atom, Member = member })
                .OnConflict(row => new { row.Atom, row.Member })
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Retains the first exact atom reference for one series dimension.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="group">The registered direct-summary group identity.</param>
        /// <param name="dimension">The stable dimension ordinal.</param>
        /// <param name="atom">The interned exact dimension atom identity.</param>
        /// <param name="kindCode">The stored dimension-kind code.</param>
        /// <returns>The affected-row count; ignored duplicate members return zero.</returns>
        public static SqlQuery<int> AddDirectReference(long series, int group, int dimension, long atom, int kindCode)
        {
            return Sql.Insert(new DirectDimensionRefRow { Series = series, Group = group, Dimension = dimension, Atom = atom, KindCode = kindCode })
                .OnConflict(row => new { row.Series, row.Dimension })
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads native and decimal overflow counts for all four direct coverage classes.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<DirectCoverageRecord?> ReadDirectCoverage(long series, int measure)
        {
            return Sql.From<DirectCoverageRow>()
                .Where(row => row.Series == series && row.Measure == measure)
                .Select(row => new DirectCoverageRecord { KnownInteger = row.KnownInteger, KnownWide = row.KnownWide, UnknownInteger = row.UnknownInteger, UnknownWide = row.UnknownWide, UnsupportedInteger = row.UnsupportedInteger, UnsupportedWide = row.UnsupportedWide, NotApplicableInteger = row.NotApplicableInteger, NotApplicableWide = row.NotApplicableWide })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts coverage while preserving the original group and declared measure kind.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="group">The registered direct-summary group identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="kind">The declared scalar, set or rational measure kind.</param>
        /// <param name="presence">The known-value presence flag, zero or one.</param>
        /// <param name="knownInteger">The native known count, or null for decimal overflow.</param>
        /// <param name="knownWide">The exact known overflow count text, or null for a native integer.</param>
        /// <param name="unknownInteger">The native unknown count, or null for decimal overflow.</param>
        /// <param name="unknownWide">The exact unknown overflow count text, or null for a native integer.</param>
        /// <param name="unsupportedInteger">The native unsupported count, or null for decimal overflow.</param>
        /// <param name="unsupportedWide">The exact unsupported overflow count text, or null for a native integer.</param>
        /// <param name="notApplicableInteger">The native not-applicable count, or null for decimal overflow.</param>
        /// <param name="notApplicableWide">The exact not-applicable overflow count text, or null for a native integer.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveDirectCoverage(long series, int group, int measure, string kind, int presence, long? knownInteger, string? knownWide, long? unknownInteger, string? unknownWide, long? unsupportedInteger, string? unsupportedWide, long? notApplicableInteger, string? notApplicableWide, byte[] revision)
        {
            return Sql.Insert(new DirectCoverageRow { Series = series, Group = group, Measure = measure, Kind = kind, Presence = presence, KnownInteger = knownInteger, KnownWide = knownWide, UnknownInteger = unknownInteger, UnknownWide = unknownWide, UnsupportedInteger = unsupportedInteger, UnsupportedWide = unsupportedWide, NotApplicableInteger = notApplicableInteger, NotApplicableWide = notApplicableWide, Revision = revision })
                .OnConflict(row => new { row.Series, row.Measure })
                .UpdateFromInserted(row => new { row.Presence, row.KnownInteger, row.KnownWide, row.UnknownInteger, row.UnknownWide, row.UnsupportedInteger, row.UnsupportedWide, row.NotApplicableInteger, row.NotApplicableWide, row.Revision })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Reads an exact direct scalar without reconstructing historical archives.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<DirectScalarRecord?> ReadScalar(long series, int measure)
        {
            return Sql.From<DirectScalarRow>()
                .Where(row => row.Series == series && row.Measure == measure)
                .Select(row => new DirectScalarRecord { Integer = row.Integer, Wide = row.Wide })
                .FirstOrDefault();
        }

        /// <summary>
        /// Reads both exact rational terms without floating-point conversion.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <returns>The selected exact values, or null when no matching row exists.</returns>
        public static SqlQuery<DirectRatioRecord?> ReadRatio(long series, int measure)
        {
            return Sql.From<DirectRatioRow>()
                .Where(row => row.Series == series && row.Measure == measure)
                .Select(row => new DirectRatioRecord { Numerator = row.Numerator, NumeratorWide = row.NumeratorWide, Denominator = row.Denominator, DenominatorWide = row.DenominatorWide })
                .FirstOrDefault();
        }

        /// <summary>
        /// Upserts an exact rational extremum and its source witness while retaining the original group.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="group">The registered direct-summary group identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="numerator">The native nonnegative numerator, or null for decimal overflow.</param>
        /// <param name="numeratorWide">The exact numerator overflow text, or null for a native integer.</param>
        /// <param name="denominator">The native positive denominator, or null for decimal overflow.</param>
        /// <param name="denominatorWide">The exact denominator overflow text, or null for a native integer.</param>
        /// <param name="witness">The retained source witness identity.</param>
        /// <returns>The provider affected-row count.</returns>
        public static SqlQuery<int> SaveRatio(long series, int group, int measure, long? numerator, string? numeratorWide, long? denominator, string? denominatorWide, long witness)
        {
            return Sql.Insert(new DirectRatioRow { Series = series, Group = group, Measure = measure, Numerator = numerator, NumeratorWide = numeratorWide, Denominator = denominator, DenominatorWide = denominatorWide, Witness = witness })
                .OnConflict(row => new { row.Series, row.Measure })
                .UpdateFromInserted(row => new { row.Numerator, row.NumeratorWide, row.Denominator, row.DenominatorWide, row.Witness })
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Adds exact direct membership while retaining original metadata on duplicate members.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="group">The registered direct-summary group identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="member">The immutable dictionary member identity.</param>
        /// <returns>The affected-row count; ignored duplicate members return zero.</returns>
        public static SqlQuery<int> AddDirectSetMember(long series, int group, int measure, long member)
        {
            return Sql.Insert(new DirectSetMemberRow { Series = series, Group = group, Measure = measure, Namespace = _nativeMember, Member = member })
                .OnConflict(row => new { row.Series, row.Measure, row.Namespace, row.Member })
                .DoNothing()
                .ExecuteNonQuery();
        }

        /// <summary>
        /// Resolves the active direct context without replacing its original scope.
        /// </summary>
        /// <param name="kind">The declared direct context kind.</param>
        /// <param name="scope">The interned lifetime, attempt or battle scope.</param>
        /// <param name="key">The complete canonical binary key.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternDirectContext(string kind, long scope, byte[] key)
        {
            return Sql.Insert(new DirectContextRow { Meta = 1, Kind = kind, Scope = scope, Key = key, State = _activeContextState })
                .OnConflict(row => new { row.Kind, row.Key })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Interns the complete exact dimension atom without mutating an existing value.
        /// </summary>
        /// <param name="kind">The declared exact dimension value kind.</param>
        /// <param name="coverage">The known or unavailable evidence class.</param>
        /// <param name="integer">The native signed value, or null for another value kind.</param>
        /// <param name="text">The exact text value, or null for another value kind.</param>
        /// <param name="blob">The exact binary value, or null for another value kind.</param>
        /// <param name="numerator">The exact nonnegative rational numerator, or null for another value kind.</param>
        /// <param name="denominator">The exact positive rational denominator, or null for another value kind.</param>
        /// <param name="value">The immutable dictionary identity for the complete typed atom.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternAtom(string kind, string coverage, long? integer, string? text, byte[]? blob, long? numerator, long? denominator, long value)
        {
            return Sql.Insert(new DirectAtomRow { Kind = kind, Coverage = coverage, Integer = integer, Text = text, Blob = blob, Numerator = numerator, Denominator = denominator, Value = value })
                .OnConflict(row => row.Value)
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Resolves a direct summary series and advances only its collection revision.
        /// </summary>
        /// <param name="group">The registered direct-summary group identity.</param>
        /// <param name="context">The interned direct summary context.</param>
        /// <param name="key">The complete canonical binary key.</param>
        /// <param name="revision">The exact eight-byte collection revision.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> TouchDirectSeries(int group, long context, byte[] key, byte[] revision)
        {
            return Sql.Insert(new DirectSeriesRow { Group = group, Context = context, Format = 2, Key = key, Revision = revision })
                .OnConflict(row => new { row.Group, row.Context, row.Key })
                .UpdateFromInserted(row => row.Revision)
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Retains the actual winning source receipt and resolves its existing or new witness identity.
        /// </summary>
        /// <param name="series">The exact dimension-series identity.</param>
        /// <param name="group">The registered direct-summary group identity.</param>
        /// <param name="measure">The stable measure ordinal.</param>
        /// <param name="attempt">The sixteen-byte owning attempt identity, or null when absent.</param>
        /// <param name="stream">The exact sixteen-byte producer stream identity.</param>
        /// <param name="sequence">The exact eight-byte source sequence.</param>
        /// <param name="revision">The exact eight-byte source collection revision.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> SaveWitness(long series, int group, int measure, byte[]? attempt, byte[] stream, byte[] sequence, byte[] revision)
        {
            return Sql.Insert(new DirectWitnessRow { Series = series, Group = group, Measure = measure, Slot = 0, Kind = _sourceFactWitnessKind, Attempt = attempt, Stream = stream, Sequence = sequence, Revision = revision })
                .OnConflict(row => new { row.Series, row.Measure, row.Slot })
                .UpdateFromInserted(row => new { row.Attempt, row.Stream, row.Sequence, row.Revision })
                .ResolveId(row => row.Id);
        }
    }
}
