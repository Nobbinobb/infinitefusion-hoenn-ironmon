using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes one current or stored move belonging to the active player Pokemon.
/// </summary>
public sealed class PlayerMoveSnapshot
{
    private MovePowerPresentationSnapshot? _resolvedPowerPresentation;
    private bool _powerPresentationResolved;
    /// <summary>
    /// Initializes an empty player move snapshot for protocol serialization.
    /// </summary>
    public PlayerMoveSnapshot()
    {
    }

    /// <summary>
    /// Gets or initializes the stable move identifier.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets or initializes the localized move name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or initializes the stable move type identifier.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// Gets or initializes the move damage category.
    /// </summary>
    public MoveCategory Category { get; init; }

    /// <summary>
    /// Gets or initializes the localized move description.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initializes the current PP.
    /// </summary>
    public int CurrentPp { get; init; }

    /// <summary>
    /// Gets or initializes the maximum PP after PP increases.
    /// </summary>
    public int TotalPp { get; init; }

    /// <summary>
    /// Gets or initializes the transmitted base power, with any specialized power display supplied separately.
    /// </summary>
    public int Power { get; init; }

    /// <summary>
    /// Gets or initializes the explicit power presentation, including a presentation completed locally by the tracker.
    /// </summary>
    /// <remarks>
    /// An existing explicit presentation takes precedence when a deferred display is completed.
    /// The resolved value is serialized with the move so its display does not depend on later catalogue changes.
    /// </remarks>
    public MovePowerPresentationSnapshot? PowerPresentation { get => _resolvedPowerPresentation ?? field; init; }

    /// <summary>
    /// Gets or initializes the game's marker requesting completion of a stateless power display.
    /// </summary>
    /// <remarks>
    /// Local completion makes this property return false. False values are omitted from serialized moves.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PowerPresentationDeferred { get => !_powerPresentationResolved && field; init; }

    /// <summary>
    /// Records a completed power presentation and suppresses the deferral marker before publication or persistence.
    /// </summary>
    /// <remarks>
    /// Any existing explicit presentation takes precedence over the supplied value, including on repeated completion.
    /// </remarks>
    /// <param name="presentation">The resolved display, including a safe unknown fallback.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="presentation"/> is <see langword="null"/>.</exception>
    public void CompletePowerPresentation(MovePowerPresentationSnapshot presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        _resolvedPowerPresentation = PowerPresentation ?? presentation;
        _powerPresentationResolved = true;
    }

    /// <summary>
    /// Gets or initializes the accuracy, where zero represents an always-hit move.
    /// </summary>
    public int Accuracy { get; init; }
}
