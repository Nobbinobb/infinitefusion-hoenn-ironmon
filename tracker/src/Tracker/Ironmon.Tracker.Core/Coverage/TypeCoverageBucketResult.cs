using Ironmon.Tracker.Core.Moves;

namespace Ironmon.Tracker.Core.Coverage;

/// <summary>
/// Describes one calculated effectiveness bucket.
/// </summary>
/// <remarks>
/// Initializes one calculated effectiveness bucket.
/// </remarks>
/// <param name="effectiveness">The bucket's damage multiplier.</param>
/// <param name="count">The current raw population count.</param>
/// <param name="percentage">The policy-weighted percentage from zero through one hundred.</param>
public sealed class TypeCoverageBucketResult(MoveEffectiveness effectiveness, long count, decimal percentage)
{
    /// <summary>
    /// Gets the bucket's damage multiplier.
    /// </summary>
    public MoveEffectiveness Effectiveness { get; } = effectiveness;

    /// <summary>
    /// Gets the current raw population count.
    /// </summary>
    public long Count { get; } = count;

    /// <summary>
    /// Gets the policy-weighted percentage from zero through one hundred.
    /// </summary>
    public decimal Percentage { get; } = percentage;
}
