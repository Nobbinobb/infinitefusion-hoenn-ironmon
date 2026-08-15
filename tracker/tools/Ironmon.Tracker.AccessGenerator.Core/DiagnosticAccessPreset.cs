namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Describes one local generator preset that prefills an editable capability selection.
/// </summary>
/// <param name="id">The stable local preset identifier.</param>
/// <param name="name">The user-facing preset name.</param>
/// <param name="description">The user-facing preset description.</param>
/// <param name="capabilities">The direct capabilities selected by the preset.</param>
/// <exception cref="ArgumentException">Thrown when an identifier, name, or description is empty.</exception>
/// <exception cref="ArgumentNullException">Thrown when capabilities is null.</exception>
/// <remarks>Initializes an immutable local generator preset.</remarks>
public sealed class DiagnosticAccessPreset(string id, string name, string description, IReadOnlyList<string> capabilities)
{
    /// <summary>
    /// Gets the stable local preset identifier.
    /// </summary>
    public string Id { get; } = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("A preset ID is required.", nameof(id)) : id;

    /// <summary>
    /// Gets the user-facing preset name.
    /// </summary>
    public string Name { get; } = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A preset name is required.", nameof(name)) : name;

    /// <summary>
    /// Gets the user-facing preset description.
    /// </summary>
    public string Description { get; } = string.IsNullOrWhiteSpace(description) ? throw new ArgumentException("A preset description is required.", nameof(description)) : description;

    /// <summary>
    /// Gets the direct capabilities selected by the preset.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; } = [.. capabilities ?? throw new ArgumentNullException(nameof(capabilities))];
}
