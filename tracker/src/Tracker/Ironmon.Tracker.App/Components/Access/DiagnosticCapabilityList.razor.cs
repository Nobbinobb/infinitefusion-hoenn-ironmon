using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Access;

/// <summary>
/// Displays localized diagnostic permissions in the shared direct and included grant layout.
/// </summary>
public partial class DiagnosticCapabilityList
{
    /// <summary>
    /// Gets or sets the supported capability identifiers to display.
    /// </summary>
    [Parameter]
    public IReadOnlyList<string> Capabilities { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the capabilities are implied by broader direct grants.
    /// </summary>
    [Parameter]
    public bool Included { get; set; }
}
