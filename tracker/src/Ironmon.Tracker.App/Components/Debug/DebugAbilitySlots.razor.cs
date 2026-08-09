using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

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
    private string GetGroupName(DebugAbilitySlotGroup group) => group switch
    {
        DebugAbilitySlotGroup.Current => Text["Debug.Abilities.CurrentHeading"],
        DebugAbilitySlotGroup.Generated => Text["Debug.Abilities.GeneratedSlotsHeading"],
        DebugAbilitySlotGroup.FinalFusion => Text["Debug.Abilities.FinalFusionSlotsHeading"],
        DebugAbilitySlotGroup.BodyGenerated => Text["Debug.Abilities.BodyGeneratedHeading"],
        DebugAbilitySlotGroup.HeadGenerated => Text["Debug.Abilities.HeadGeneratedHeading"],
        _ => Text["Debug.Abilities.AbilitiesHeading"]
    };

    /// <summary>
    /// Gets the visible normal or hidden slot label.
    /// </summary>
    /// <param name="slot">The ability slot.</param>
    /// <returns>The slot label.</returns>
    private string GetSlotName(DebugAbilitySlotSnapshot slot)
        => Text["Debug.Abilities.AbilitySlotName", slot.Kind == DebugAbilitySlotKind.Hidden ? Text["Debug.Abilities.Hidden"] : Text["Debug.Abilities.Normal"], slot.Index];

    /// <summary>
    /// Gets the visible eligibility label.
    /// </summary>
    /// <param name="eligibility">The ability eligibility.</param>
    /// <returns>The eligibility label.</returns>
    private string GetEligibilityName(DebugAbilityEligibility eligibility) => eligibility switch
    {
        DebugAbilityEligibility.ExactSpecies => Text["Debug.Abilities.ExactSpecies"],
        DebugAbilityEligibility.ComponentCompatible => Text["Debug.Abilities.ComponentCompatible"],
        DebugAbilityEligibility.Universal => Text["Debug.Abilities.Universal"],
        _ => Text["Debug.Abilities.None"]
    };
}
