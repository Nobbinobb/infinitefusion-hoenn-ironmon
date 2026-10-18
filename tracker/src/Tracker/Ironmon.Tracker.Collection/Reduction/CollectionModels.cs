using System.Numerics;
using System.Text.Json;

namespace Ironmon.Tracker.Collection.Reduction;

/// <summary>
/// Describes one generic accumulated measurement.
/// </summary>
/// <remarks>
/// Initializes a measurement declaration from its stable ordinal, typed merge operation and unit.
/// </remarks>
/// <param name="Id">Stable ordinal within its family.</param>
/// <param name="Key">Generic measurement key.</param>
/// <param name="Kind">Physical value class.</param>
/// <param name="MergeOperator">Exact merge operation.</param>
/// <param name="Unit">Measurement unit.</param>
public sealed record CollectionMeasureDefinition(int Id, string Key, string Kind, string MergeOperator, string Unit);

/// <summary>
/// Describes a generic collection family and its executable source routing.
/// </summary>
/// <remarks>
/// Initializes a family declaration with its source event, storage scopes, typed measures and executable selectors.
/// </remarks>
/// <param name="Id">Stable family identifier.</param>
/// <param name="Key">Generic family key.</param>
/// <param name="OwnerEvent">Authoritative source event type.</param>
/// <param name="StorageKind">Persistent or bounded transient state.</param>
/// <param name="Scopes">Declared collection scopes.</param>
/// <param name="Measures">Typed measure declarations.</param>
/// <param name="Definition">Executable selectors and eligibility guards.</param>
public sealed record CollectionFamilyDefinition(int Id, string Key, string OwnerEvent, string StorageKind, IReadOnlyList<string> Scopes, IReadOnlyList<CollectionMeasureDefinition> Measures, JsonElement Definition);

/// <summary>
/// Retains an exact typed dimension, including unavailable values.
/// </summary>
/// <remarks>
/// Initializes an exact dimension value while retaining its type and evidence coverage independently.
/// </remarks>
/// <param name="Key">Dimension key.</param>
/// <param name="Kind">Typed value class.</param>
/// <param name="Coverage">Known, unknown, unsupported, or not applicable.</param>
/// <param name="Value">Exact value, or JSON null when unavailable.</param>
public sealed record CollectionDimensionValue(string Key, string Kind, string Coverage, JsonElement Value);

/// <summary>
/// Applies one typed update without converting unavailable evidence to zero.
/// </summary>
/// <remarks>
/// Initializes one scalar, set, ordered-state or rational update with its declared merge operation and coverage.
/// </remarks>
/// <param name="Key">Measure key.</param>
/// <param name="Kind">Scalar, exact set, ordered payload, or rational extremum.</param>
/// <param name="MergeOperator">Exact merge operation.</param>
/// <param name="Coverage">Evidence coverage.</param>
/// <param name="Scalar">Arbitrary precision integer when applicable.</param>
/// <param name="Members">Exact set members when applicable.</param>
/// <param name="OrderedState">Ordered or rational value when applicable.</param>
public sealed record CollectionMeasureUpdate(string Key, string Kind, string MergeOperator, string Coverage, BigInteger? Scalar, IReadOnlyList<JsonElement>? Members, JsonElement? OrderedState);

/// <summary>
/// Contains the generic family effects of one authoritative fact.
/// </summary>
/// <remarks>
/// Initializes the atomic family update for one occurrence from its declared family, dimensions and measures.
/// </remarks>
/// <param name="FamilyKey">Declared family key.</param>
/// <param name="Dimensions">Joint dimensions for this occurrence.</param>
/// <param name="Measures">Atomic measure updates.</param>
public sealed record CollectionProjection(string FamilyKey, IReadOnlyList<CollectionDimensionValue> Dimensions, IReadOnlyList<CollectionMeasureUpdate> Measures);

/// <summary>
/// Contains a directly indexed sufficient summary of the same factual occurrence.
/// </summary>
/// <remarks>
/// Initializes a directly indexed summary update with its stable group and exact typed values.
/// </remarks>
/// <param name="GroupId">Stable generic summary group identifier.</param>
/// <param name="Dimensions">Joint typed dimensions.</param>
/// <param name="Measures">Atomic sufficient-summary updates.</param>
public sealed record CollectionDirectProjection(string GroupId, IReadOnlyList<CollectionDimensionValue> Dimensions, IReadOnlyList<CollectionMeasureUpdate> Measures);

/// <summary>
/// Contains family and directly indexed effects from one reduction.
/// </summary>
/// <remarks>
/// Initializes the family and directly indexed outputs produced from the same source occurrence.
/// </remarks>
/// <param name="Families">Generic accumulated effects.</param>
/// <param name="Direct">Directly indexed sufficient summaries.</param>
public sealed record CollectionReduction(IReadOnlyList<CollectionProjection> Families, IReadOnlyList<CollectionDirectProjection> Direct);
