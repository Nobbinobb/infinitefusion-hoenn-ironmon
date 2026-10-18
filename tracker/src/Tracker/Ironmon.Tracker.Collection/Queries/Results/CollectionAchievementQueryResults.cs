namespace Ironmon.Tracker.Collection;

/// <summary>
/// Carries one authenticated encrypted consumer checkpoint.
/// </summary>
internal sealed class AchievementStateRecord
{
    /// <summary>
    /// Gets or initializes the sixteen-byte opaque consumer identity.
    /// </summary>
    public required byte[] Key { get; init; }

    /// <summary>
    /// Gets or initializes the thirty-two-byte definition-pack digest authenticated with this checkpoint.
    /// </summary>
    public required byte[] Binding { get; init; }

    /// <summary>
    /// Gets or initializes the monotonic private-checkpoint revision.
    /// </summary>
    public required long StateRevision { get; init; }

    /// <summary>
    /// Gets or initializes the eight-byte collection revision evaluated by this checkpoint.
    /// </summary>
    public required byte[] Through { get; init; }

    /// <summary>
    /// Gets or initializes the encrypted-checkpoint format version.
    /// </summary>
    public required int Format { get; init; }

    /// <summary>
    /// Gets or initializes the encryption-key slot identifier.
    /// </summary>
    public required int Slot { get; init; }

    /// <summary>
    /// Gets or initializes the twelve-byte AES-GCM nonce.
    /// </summary>
    public required byte[] Nonce { get; init; }

    /// <summary>
    /// Gets or initializes the sixteen-byte AES-GCM authentication tag.
    /// </summary>
    public required byte[] Tag { get; init; }

    /// <summary>
    /// Gets or initializes the encrypted unlock ledger and pending notification identities.
    /// </summary>
    public required byte[] Ciphertext { get; init; }
}

/// <summary>
/// Carries an interned typed dimension of one correlated summary series.
/// </summary>
internal sealed class AchievementDimensionRecord
{
    /// <summary>
    /// Gets or initializes the exact correlated summary-series identity.
    /// </summary>
    public required long Series { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based dimension ordinal within its generic group.
    /// </summary>
    public required int Dimension { get; init; }

    /// <summary>
    /// Gets or initializes the interned typed atom value identity.
    /// </summary>
    public required long Value { get; init; }
}

/// <summary>
/// Carries an exact scalar using its compact or arbitrary-precision representation.
/// </summary>
internal sealed class AchievementScalarRecord
{
    /// <summary>
    /// Gets or initializes the exact correlated summary-series identity.
    /// </summary>
    public required long Series { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based measure ordinal within its generic group.
    /// </summary>
    public required int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the exact signed 64-bit value, or null when the wide decimal is used.
    /// </summary>
    public required long? Integer { get; init; }

    /// <summary>
    /// Gets or initializes the arbitrary-precision signed decimal, or null when the 64-bit value is used.
    /// </summary>
    public required string? Wide { get; init; }
}

/// <summary>
/// Carries one exact set membership from a generic direct summary.
/// </summary>
internal sealed class AchievementSetRecord
{
    /// <summary>
    /// Gets or initializes the exact correlated summary-series identity.
    /// </summary>
    public required long Series { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based measure ordinal within its generic group.
    /// </summary>
    public required int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the interned exact set-member value identity.
    /// </summary>
    public required long Member { get; init; }
}

/// <summary>
/// Carries one exact rational extremum without floating-point conversion.
/// </summary>
internal sealed class AchievementRatioRecord
{
    /// <summary>
    /// Gets or initializes the exact correlated summary-series identity.
    /// </summary>
    public required long Series { get; init; }

    /// <summary>
    /// Gets or initializes the zero-based measure ordinal within its generic group.
    /// </summary>
    public required int Measure { get; init; }

    /// <summary>
    /// Gets or initializes the exact 64-bit numerator, or null when the wide numerator is used.
    /// </summary>
    public required long? Numerator { get; init; }

    /// <summary>
    /// Gets or initializes the arbitrary-precision numerator decimal, or null for a 64-bit numerator.
    /// </summary>
    public required string? NumeratorWide { get; init; }

    /// <summary>
    /// Gets or initializes the positive 64-bit denominator, or null when the wide denominator is used.
    /// </summary>
    public required long? Denominator { get; init; }

    /// <summary>
    /// Gets or initializes the arbitrary-precision positive denominator decimal, or null for a 64-bit denominator.
    /// </summary>
    public required string? DenominatorWide { get; init; }
}
