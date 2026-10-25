namespace Ironmon.Tracker.Access;

/// <summary>
/// Describes one selectable Ironmon diagnostic capability.
/// </summary>
/// <param name="id">The stable serialized capability identifier.</param>
/// <param name="displayName">The concise user-facing capability name.</param>
/// <param name="description">The user-facing explanation of the granted information.</param>
/// <param name="includedCapabilities">Capabilities automatically included by this capability.</param>
/// <exception cref="ArgumentException">Thrown when an identifier, name, or description is empty.</exception>
/// <remarks>Initializes an immutable diagnostic capability definition.</remarks>
public sealed class DiagnosticCapabilityDefinition(string id, string displayName, string description, IReadOnlyList<string>? includedCapabilities = null)
{
    /// <summary>
    /// Gets the stable serialized capability identifier.
    /// </summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A capability ID is required.", nameof(id)) : id;

    /// <summary>
    /// Gets the concise user-facing capability name.
    /// </summary>
    public string DisplayName { get; } = string.IsNullOrWhiteSpace(displayName) ? throw new ArgumentException("A capability display name is required.", nameof(displayName)) : displayName;

    /// <summary>
    /// Gets the user-facing explanation of the granted information.
    /// </summary>
    public string Description { get; } = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("A capability description is required.", nameof(description)) : description;

    /// <summary>
    /// Gets capabilities automatically included by this capability.
    /// </summary>
    public IReadOnlyList<string> IncludedCapabilities { get; } = includedCapabilities is null ? [] : [.. includedCapabilities];
}
