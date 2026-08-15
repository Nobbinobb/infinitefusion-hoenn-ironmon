namespace Ironmon.Tracker.AccessGenerator;

/// <summary>
/// Tracks editable direct grants while presenting implied capabilities as selected and locked.
/// </summary>
public sealed class DiagnosticAccessSelection
{
    private readonly HashSet<string> _direct = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes an empty diagnostic capability selection.
    /// </summary>
    public DiagnosticAccessSelection()
    {
    }

    /// <summary>
    /// Gets the stable direct capability selection.
    /// </summary>
    public IReadOnlyList<string> DirectCapabilities => [.. _direct.Order(StringComparer.Ordinal)];

    /// <summary>
    /// Gets the stable effective capability selection after implication expansion.
    /// </summary>
    public IReadOnlyList<string> EffectiveCapabilities => DiagnosticCapabilityCatalog.Expand(_direct);

    /// <summary>
    /// Gets whether one capability is directly selected or implied.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether the capability is effective.</returns>
    public bool IsSelected(string capability) => EffectiveCapabilities.Contains(capability, StringComparer.Ordinal);

    /// <summary>
    /// Gets whether one selected capability is included by another direct grant.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <returns>Whether the capability is selected only through implication.</returns>
    public bool IsIncluded(string capability) => !_direct.Contains(capability) && IsSelected(capability);

    /// <summary>
    /// Applies a direct capability checkbox change while preserving implication rules.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <param name="selected">Whether the capability should be selected.</param>
    /// <exception cref="ArgumentException">Thrown when the capability is unsupported.</exception>
    public void Set(string capability, bool selected)
    {
        if (!DiagnosticCapabilityCatalog.IsKnown(capability))
            throw new ArgumentException($"Unsupported diagnostic capability '{capability}'.", nameof(capability));

        if (!selected && IsIncluded(capability))
            return;

        if (selected)
        {
            _direct.Add(capability);
            if (capability == DiagnosticCapabilities.PokemonAllActive)
            {
                _direct.Remove(DiagnosticCapabilities.PokemonCurrentPlayer);
                _direct.Remove(DiagnosticCapabilities.PokemonCurrentEnemies);
            }
        }
        else
        {
            _direct.Remove(capability);
        }
    }

    /// <summary>
    /// Replaces the direct selection with one preset or restored configuration.
    /// </summary>
    /// <param name="capabilities">The supported direct capability identifiers.</param>
    /// <exception cref="ArgumentNullException">Thrown when capabilities is null.</exception>
    /// <exception cref="ArgumentException">Thrown when a capability is unsupported.</exception>
    public void Replace(IEnumerable<string> capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        string[] requested = [.. capabilities];
        string? unsupported = requested.FirstOrDefault(capability => !DiagnosticCapabilityCatalog.IsKnown(capability));
        if (unsupported is not null)
            throw new ArgumentException($"Unsupported diagnostic capability '{unsupported}'.", nameof(capabilities));

        _direct.Clear();
        foreach (string capability in requested)
            _direct.Add(capability);

        if (_direct.Contains(DiagnosticCapabilities.PokemonAllActive))
        {
            _direct.Remove(DiagnosticCapabilities.PokemonCurrentPlayer);
            _direct.Remove(DiagnosticCapabilities.PokemonCurrentEnemies);
        }
    }

    /// <summary>
    /// Clears every direct and effective selection.
    /// </summary>
    public void Clear()
        => _direct.Clear();
}
