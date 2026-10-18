using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Starter;

/// <summary>
/// Displays one starter with separate selection and sprite-preview actions.
/// </summary>
public partial class StarterChoiceCard
{
    /// <summary>
    /// Gets or sets the legally visible starter information.
    /// </summary>
    [Parameter, EditorRequired]
    public StarterChoiceSnapshot Choice { get; set; } = null!;

    /// <summary>
    /// Gets or sets whether this slot is the deterministic random pick.
    /// </summary>
    [Parameter]
    public bool RandomPick { get; set; }

    /// <summary>
    /// Gets or sets the optional inclusive BST ceiling.
    /// </summary>
    [Parameter]
    public int? Ceiling { get; set; }

    /// <summary>
    /// Gets or sets whether automatic-selection restrictions apply.
    /// </summary>
    [Parameter]
    public bool AutoSelect { get; set; }

    /// <summary>
    /// Gets or sets the game directory for sprite loading.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets whether the selection action is currently available.
    /// </summary>
    [Parameter]
    public bool CanSelect { get; set; }

    /// <summary>
    /// Gets or sets the callback requesting selection confirmation.
    /// </summary>
    [Parameter]
    public EventCallback Selected { get; set; }
}
