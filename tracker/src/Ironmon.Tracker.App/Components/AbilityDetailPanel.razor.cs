using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Displays all legally known information for a selected ability.
/// </summary>
public partial class AbilityDetailPanel
{
    /// <summary>
    /// Gets or sets the selected ability.
    /// </summary>
    [Parameter]
    public AbilitySnapshot Ability { get; set; } = null!;

    /// <summary>
    /// Gets or sets the callback raised when the panel should close.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Closes the ability-information panel.
    /// </summary>
    /// <returns>A task representing callback delivery.</returns>
    private Task Close()
        => Closed.InvokeAsync();

    /// <summary>
    /// Gets a non-empty ability description.
    /// </summary>
    /// <returns>The localized description or fallback.</returns>
    private string GetDescription()
        => string.IsNullOrWhiteSpace(Ability.Description) ? "No description is available." : Ability.Description;
}
