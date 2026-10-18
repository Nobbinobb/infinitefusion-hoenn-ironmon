using Ironmon.Sqlite.Querying;

namespace Ironmon.Tracker.Collection;

/// <summary>
/// Defines generated identity storage operations.
/// </summary>
internal static partial class CollectionQueries
{
    private const string _identityNamespaceRevision = "1";
    private const string _localDefinitionOrigin = "local_definition";

    /// <summary>
    /// Declares typed identity queries for compile-time generation.
    /// </summary>
    private static partial class Definitions
    {
        /// <summary>
        /// Interns a native identity namespace without changing an existing revision binding.
        /// </summary>
        /// <param name="name">The exact identity namespace name.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternNamespace(string name)
        {
            return Sql.Insert(new IdentityNamespaceRow { Revision = _identityNamespaceRevision, Name = name })
                .OnConflict(row => new { row.Name, row.Revision })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Interns an exact dictionary identity under its complete native namespace.
        /// </summary>
        /// <param name="nameSpace">The owning identity namespace.</param>
        /// <param name="value">The immutable dictionary value identity.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternIdentity(long nameSpace, long value)
        {
            return Sql.Insert(new NativeIdentityRow { Namespace = nameSpace, Value = value })
                .OnConflict(row => new { row.Namespace, row.Value })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Interns an ordered dimension tuple while retaining existing arity metadata.
        /// </summary>
        /// <param name="key">The complete canonical binary key.</param>
        /// <param name="arity">The number of ordered tuple dimensions.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternTuple(byte[] key, int arity)
        {
            return Sql.Insert(new DimensionTupleRow { Format = 1, Key = key, Arity = arity })
                .OnConflict(row => new { row.Format, row.Key })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Interns a real scope without replacing its retained parent or attempt metadata.
        /// </summary>
        /// <param name="kind">The declared scope kind.</param>
        /// <param name="key">The complete exact binary scope key.</param>
        /// <param name="parent">The parent lifetime scope, or null for a root.</param>
        /// <param name="attempt">The sixteen-byte owning attempt identity, or null when absent.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternScope(string kind, byte[] key, long? parent, byte[]? attempt)
        {
            return Sql.Insert(new CollectionScopeRow { Kind = kind, Key = key, Parent = parent, Attempt = attempt })
                .OnConflict(row => new { row.Kind, row.Key })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Interns an immutable context without overwriting retained definition metadata.
        /// </summary>
        /// <param name="kind">The declared context kind.</param>
        /// <param name="key">The complete canonical binary key.</param>
        /// <param name="digest">The complete context key digest.</param>
        /// <param name="block">The immutable context definition block.</param>
        /// <returns>The signed 64-bit matching identity, whether the row was inserted or already existed.</returns>
        public static SqlQuery<long> InternContext(string kind, byte[] key, byte[] digest, long block)
        {
            return Sql.Insert(new CollectionContextRow { Kind = kind, Key = key, Digest = digest, Format = 1, Origin = _localDefinitionOrigin, Block = block })
                .OnConflict(row => new { row.Kind, row.Format, row.Key })
                .DoNothing()
                .ResolveId(row => row.Id);
        }

        /// <summary>
        /// Adds exact tuple components without replacing an already populated slot.
        /// </summary>
        /// <param name="tuple">The exact tuple value retained by the original operation.</param>
        /// <param name="group">The exact group value retained by the original operation.</param>
        /// <param name="zero">The exact zero value retained by the original operation.</param>
        /// <param name="one">The exact one value retained by the original operation.</param>
        /// <param name="two">The exact two value retained by the original operation.</param>
        /// <param name="three">The exact three value retained by the original operation.</param>
        /// <returns>The original typed result within the caller-owned transaction.</returns>
        public static SqlQuery<int> SaveTupleGroup(long tuple, long group, long? zero, long? one, long? two, long? three)
        {
            return Sql.Insert(new TupleGroupWriteRow { Tuple = tuple, Group = group, Zero = zero, One = one, Two = two, Three = three })
                .OnConflict(row => new { row.Tuple, row.Group })
                .Update((current, incoming) => new TupleGroupWriteRow { Zero = current.Zero ?? incoming.Zero, One = current.One ?? incoming.One, Two = current.Two ?? incoming.Two, Three = current.Three ?? incoming.Three })
                .ExecuteNonQuery();
        }
    }
}
