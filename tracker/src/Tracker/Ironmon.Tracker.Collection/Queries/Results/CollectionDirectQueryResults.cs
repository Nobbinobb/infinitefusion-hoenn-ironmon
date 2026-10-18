namespace Ironmon.Tracker.Collection;

/// <summary>
/// Carries exactly the stored columns selected by the DirectAtomRecord query projection.
/// </summary>
internal sealed class DirectAtomRecord
{
    /// <summary>
    /// Gets or initializes the interned exact dimension atom identity.
    /// </summary>
    public required long Atom { get; init; }
}

/// <summary>
/// Carries exactly the stored columns selected by the DirectCoverageRecord query projection.
/// </summary>
internal sealed class DirectCoverageRecord
{
    /// <summary>
    /// Gets or initializes the native known count, or null for decimal overflow.
    /// </summary>
    public required long? KnownInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact known overflow count text, or null for a native integer.
    /// </summary>
    public required string? KnownWide { get; init; }

    /// <summary>
    /// Gets or initializes the native unknown count, or null for decimal overflow.
    /// </summary>
    public required long? UnknownInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact unknown overflow count text, or null for a native integer.
    /// </summary>
    public required string? UnknownWide { get; init; }

    /// <summary>
    /// Gets or initializes the native unsupported count, or null for decimal overflow.
    /// </summary>
    public required long? UnsupportedInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact unsupported overflow count text, or null for a native integer.
    /// </summary>
    public required string? UnsupportedWide { get; init; }

    /// <summary>
    /// Gets or initializes the native not-applicable count, or null for decimal overflow.
    /// </summary>
    public required long? NotApplicableInteger { get; init; }

    /// <summary>
    /// Gets or initializes the exact not-applicable overflow count text, or null for a native integer.
    /// </summary>
    public required string? NotApplicableWide { get; init; }
}

/// <summary>
/// Carries exactly the stored columns selected by the DirectScalarRecord query projection.
/// </summary>
internal sealed class DirectScalarRecord
{
    /// <summary>
    /// Gets or initializes the native signed integer, or null for decimal overflow.
    /// </summary>
    public required long? Integer { get; init; }

    /// <summary>
    /// Gets or initializes the exact decimal overflow text, or null for a native integer.
    /// </summary>
    public required string? Wide { get; init; }
}

/// <summary>
/// Carries exactly the stored columns selected by the DirectRatioRecord query projection.
/// </summary>
internal sealed class DirectRatioRecord
{
    /// <summary>
    /// Gets or initializes the native nonnegative numerator, or null for decimal overflow.
    /// </summary>
    public required long? Numerator { get; init; }

    /// <summary>
    /// Gets or initializes the exact numerator overflow text, or null for a native integer.
    /// </summary>
    public required string? NumeratorWide { get; init; }

    /// <summary>
    /// Gets or initializes the native positive denominator, or null for decimal overflow.
    /// </summary>
    public required long? Denominator { get; init; }

    /// <summary>
    /// Gets or initializes the exact denominator overflow text, or null for a native integer.
    /// </summary>
    public required string? DenominatorWide { get; init; }
}
