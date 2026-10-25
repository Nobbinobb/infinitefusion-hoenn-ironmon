namespace Ironmon.Tracker.Collection.Queries.Results;

/// <summary>
/// Carries a complete retained identity selected during migration maintenance.
/// </summary>
internal sealed class MaintenanceIdentityRecord
{
    /// <summary>
    /// Gets or initializes the complete selected SQLite integer identity.
    /// </summary>
    public required long Id { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class KeyMigrationRecord
{
    /// <summary>
    /// Gets or initializes the complete retained context, scope or individual identity.
    /// </summary>
    public required long Id { get; init; }

    /// <summary>
    /// Gets or initializes the complete original canonical key bytes.
    /// </summary>
    public required byte[] Key { get; init; }
}

/// <summary>
/// Carries exactly the selected ordered-read columns with original value precision.
/// </summary>
internal sealed class OrderedMigrationRecord
{
    /// <summary>
    /// Gets or initializes the complete family identity.
    /// </summary>
    public required long Family { get; init; }

    /// <summary>
    /// Gets or initializes the complete retained scope identity.
    /// </summary>
    public required long Scope { get; init; }

    /// <summary>
    /// Gets or initializes the complete ordered tuple identity.
    /// </summary>
    public required long Tuple { get; init; }

    /// <summary>
    /// Gets or initializes the complete original ordered-state bytes.
    /// </summary>
    public required byte[] Payload { get; init; }
}
