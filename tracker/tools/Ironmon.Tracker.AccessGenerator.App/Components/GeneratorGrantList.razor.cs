namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Renders localized direct or effective grants with their inclusion indicators.
/// </summary>
public partial class GeneratorGrantList
{
    /// <summary>
    /// Gets or sets the grants to display in their supplied order.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> Capabilities { get; set; } = [];

    /// <summary>
    /// Gets or sets the selection used to identify implied grants.
    /// </summary>
    [Parameter, EditorRequired]
    public DiagnosticAccessSelection Selection { get; set; } = null!;
}
