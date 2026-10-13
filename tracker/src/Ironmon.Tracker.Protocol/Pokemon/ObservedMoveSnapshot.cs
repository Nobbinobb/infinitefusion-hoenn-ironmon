using System.Text.Json.Serialization;

namespace Ironmon.Tracker.Protocol.Pokemon;

/// <summary>
/// Describes a legally observed player learnset move or enemy-used move and its display metadata.
/// </summary>
public sealed class ObservedMoveSnapshot
{
    private MovePowerPresentationSnapshot? _resolvedPowerPresentation;
    private bool _powerPresentationResolved;
    /// <summary>
    /// Initializes an empty observed move snapshot for protocol serialization.
    /// </summary>
    public ObservedMoveSnapshot()
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
    /// Gets or initializes the level at which the move is learned.
    /// </summary>
    public int LearnedLevel { get; init; }

    /// <summary>
    /// Gets or initializes the stable position within the generated learnset.
    /// </summary>
    public int LearnOrder { get; init; }

    /// <summary>
    /// Gets or initializes the learn source identifier.
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Gets or initializes the observation origin identifier.
    /// </summary>
    public required string Origin { get; init; }

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
    /// Gets or initializes the move's base power.
    /// </summary>
    public int Power { get; init; }

    /// <summary>
    /// Gets or initializes the explicit power presentation, including a presentation completed locally by the tracker.
    /// </summary>
    /// <remarks>
    /// An existing explicit presentation takes precedence when a deferred display is completed.
    /// The resolved value is serialized with the observation so persistent knowledge does not depend on later catalogue changes.
    /// </remarks>
    public MovePowerPresentationSnapshot? PowerPresentation { get => _resolvedPowerPresentation ?? field; init; }

    /// <summary>
    /// Gets or initializes the game's marker requesting completion of a stateless power display.
    /// </summary>
    /// <remarks>
    /// Local completion makes this property return false. False values are omitted from serialized observations.
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool PowerPresentationDeferred { get => !_powerPresentationResolved && field; init; }

    /// <summary>
    /// Gets whether this instance's power presentation has been completed locally.
    /// </summary>
    /// <remarks>
    /// This transient provenance lets knowledge comparisons refresh completed displays. It is preserved by in-memory
    /// observation copies and excluded from both persistent knowledge and wire data.
    /// </remarks>
    [JsonIgnore]
    public bool CompletedDeferredPowerPresentation => _powerPresentationResolved;

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
    /// Gets or initializes the move's accuracy.
    /// </summary>
    public int Accuracy { get; init; }

    /// <summary>
    /// Gets or initializes the move's total PP.
    /// </summary>
    public int TotalPp { get; init; }

    /// <summary>
    /// Gets or initializes the most recently observed remaining PP when known.
    /// </summary>
    public int? PpAfterUse { get; init; }
}
