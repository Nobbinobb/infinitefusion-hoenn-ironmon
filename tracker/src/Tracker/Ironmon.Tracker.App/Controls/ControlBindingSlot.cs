namespace Ironmon.Tracker.App.Controls;

/// <summary>
/// Identifies a binding slot affected by a profile conflict.
/// </summary>
/// <remarks>
/// Initializes an action and its positional binding slot.
/// </remarks>
/// <param name="action">The action identifier.</param>
/// <param name="slot">The zero-based binding slot.</param>
public sealed class ControlBindingSlot(string action, int slot)
{
    /// <summary>
    /// Gets the action identifier.
    /// </summary>
    public string Action { get; } = action;

    /// <summary>
    /// Gets the zero-based binding slot.
    /// </summary>
    public int Slot { get; } = slot;
}
