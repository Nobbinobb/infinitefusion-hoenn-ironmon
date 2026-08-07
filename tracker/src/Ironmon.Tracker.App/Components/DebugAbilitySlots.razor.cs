using Ironmon.Tracker.Protocol;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components;

/// <summary>
/// Renders grouped ability rows from the game-owned Ironmon inspector.
/// </summary>
public partial class DebugAbilitySlots
{
    /// <summary>
    /// Gets or sets the game-owned inspector snapshot.
    /// </summary>
    [Parameter]
    public DebugPokemonInspectorSnapshot Pokemon { get; set; } = null!;

    /// <summary>
    /// Gets the inspector groups that contain at least one slot.
    /// </summary>
    /// <returns>The visible groups in inspector order.</returns>
    private IReadOnlyList<DebugAbilitySlotGroup> GetVisibleGroups()
    {
        DebugAbilitySlotGroup[] order =
        [
            DebugAbilitySlotGroup.Current,
            DebugAbilitySlotGroup.Generated,
            DebugAbilitySlotGroup.FinalFusion,
            DebugAbilitySlotGroup.BodyGenerated,
            DebugAbilitySlotGroup.HeadGenerated
        ];

        return [.. order.Where(group => Pokemon.AbilitySlots.Any(slot => slot.Group == group))];
    }

    /// <summary>
    /// Gets all slots belonging to one inspector group.
    /// </summary>
    /// <param name="group">The requested group.</param>
    /// <returns>The matching slots in protocol order.</returns>
    private IReadOnlyList<DebugAbilitySlotSnapshot> GetSlots(DebugAbilitySlotGroup group)
        => [.. Pokemon.AbilitySlots.Where(slot => slot.Group == group)];

    /// <summary>
    /// Gets the visible heading for one ability group.
    /// </summary>
    /// <param name="group">The ability group.</param>
    /// <returns>The group heading.</returns>
    private static string GetGroupName(DebugAbilitySlotGroup group) => group switch
    {
        DebugAbilitySlotGroup.Current => "CURRENT",
        DebugAbilitySlotGroup.Generated => "GENERATED SLOTS",
        DebugAbilitySlotGroup.FinalFusion => "FINAL FUSION SLOTS",
        DebugAbilitySlotGroup.BodyGenerated => "BODY GENERATED",
        DebugAbilitySlotGroup.HeadGenerated => "HEAD GENERATED",
        _ => "ABILITIES"
    };

    /// <summary>
    /// Gets the visible normal or hidden slot label.
    /// </summary>
    /// <param name="slot">The ability slot.</param>
    /// <returns>The slot label.</returns>
    private static string GetSlotName(DebugAbilitySlotSnapshot slot)
        => $"{(slot.Kind == DebugAbilitySlotKind.Hidden ? "Hidden" : "Normal")} {slot.Index}";

    /// <summary>
    /// Gets the visible eligibility label.
    /// </summary>
    /// <param name="eligibility">The ability eligibility.</param>
    /// <returns>The eligibility label.</returns>
    private static string GetEligibilityName(DebugAbilityEligibility eligibility) => eligibility switch
    {
        DebugAbilityEligibility.ExactSpecies => "Exact species",
        DebugAbilityEligibility.ComponentCompatible => "Component-compatible",
        DebugAbilityEligibility.Universal => "Universal",
        _ => "None"
    };
}
