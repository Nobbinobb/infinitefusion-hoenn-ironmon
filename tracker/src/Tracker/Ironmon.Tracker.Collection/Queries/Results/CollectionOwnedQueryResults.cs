namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries exactly the columns selected by the OwnedIdentityRecord query.
/// </summary>
internal sealed class OwnedIdentityRecord
{
    /// <summary>
    /// Gets or initializes the exact owned individual identity.
    /// </summary>
    public required long Id { get; init; }
}

/// <summary>
/// Carries exactly the columns selected by the OwnedStateRecord query.
/// </summary>
internal sealed class OwnedStateRecord
{
    /// <summary>
    /// Gets or initializes the complete meaningful configuration fingerprint.
    /// </summary>
    public required byte[] Fingerprint { get; init; }

    /// <summary>
    /// Gets or initializes the last retained per-individual configuration revision number.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets or initializes the last retained complete configuration identity.
    /// </summary>
    public required long Configuration { get; init; }
}

/// <summary>
/// Carries exactly the columns selected by the MovesetIdentityRecord query.
/// </summary>
internal sealed class MovesetIdentityRecord
{
    /// <summary>
    /// Gets or initializes the exact simultaneous moveset identity.
    /// </summary>
    public required long Id { get; init; }
}

/// <summary>
/// Carries exactly the columns selected by the CompletedAttemptRecord query.
/// </summary>
internal sealed class CompletedAttemptRecord
{
    /// <summary>
    /// Gets or initializes the complete compressed sufficient attempt state.
    /// </summary>
    public required byte[] Payload { get; init; }

    /// <summary>
    /// Gets or initializes the exact uncompressed state length in bytes.
    /// </summary>
    public required long Length { get; init; }
}

/// <summary>
/// Retains the exact selected fields of a generated collection operation.
/// </summary>
internal sealed class ParentConfigurationRecord
{
    /// <summary>
    /// Gets or initializes the selected configuration value.
    /// </summary>
    public long Configuration { get; init; }
}
