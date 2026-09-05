using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Debug;

/// <summary>
/// Renders grouped ability-slot diagnostics from a live inspection or deterministic lookup.
/// </summary>
public partial class DebugAbilitySlots
{
    private DebugAbilitySlotGroup? _selectedGroup;

    /// <summary>
    /// Gets or sets whether this content uses the redesigned research presentation.
    /// </summary>
    [Parameter]
    public bool Redesigned { get; set; }

    /// <summary>
    /// Gets or sets descriptions available for the represented generated abilities.
    /// </summary>
    [Parameter]
    public IReadOnlyList<AbilitySnapshot> Abilities { get; set; } = [];

    /// <summary>
    /// Gets or sets the callback opening the selected ability's information.
    /// </summary>
    [Parameter]
    public EventCallback<AbilitySnapshot> Selected { get; set; }

    /// <summary>
    /// Gets the selected group, falling back when a different species has different groups.
    /// </summary>
    private DebugAbilitySlotGroup SelectedGroup
        => _selectedGroup is DebugAbilitySlotGroup group && GetVisibleGroups().Contains(group) ? group : GetVisibleGroups().FirstOrDefault();

    /// <summary>
    /// Opens a slot's description, preferring the complete ability catalog when available.
    /// </summary>
    /// <param name="slot">The generated slot selected by the user.</param>
    /// <returns>A task representing the containing dialog callback.</returns>
    private Task SelectSlotAsync(DebugAbilitySlotSnapshot slot)
    {
        AbilitySnapshot ability = Abilities.FirstOrDefault(value => value.Id == slot.AbilityId)
            ?? new AbilitySnapshot { Id = slot.AbilityId, Name = slot.AbilityName, Description = slot.AbilityDescription ?? Text["Redesign.Research.DescriptionUnavailable"] };

        return Selected.InvokeAsync(ability);
    }

    /// <summary>
    /// Gets or sets the ability-slot diagnostics to display.
    /// </summary>
    [Parameter]
    public IReadOnlyList<DebugAbilitySlotSnapshot> Slots { get; set; } = [];

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

        return [.. order.Where(group => Slots.Any(slot => slot.Group == group))];
    }

    /// <summary>
    /// Gets all slots belonging to one inspector group.
    /// </summary>
    /// <param name="group">The requested group.</param>
    /// <returns>The matching slots in protocol order.</returns>
    private IReadOnlyList<DebugAbilitySlotSnapshot> GetSlots(DebugAbilitySlotGroup group)
        => [.. Slots.Where(slot => slot.Group == group)];

    /// <summary>
    /// Gets the visible heading for one ability group.
    /// </summary>
    /// <param name="group">The ability group.</param>
    /// <returns>The group heading.</returns>
    private string GetGroupName(DebugAbilitySlotGroup group) => group switch
    {
        DebugAbilitySlotGroup.Current => Text[Redesigned ? "Redesign.Research.Current" : "Debug.Abilities.CurrentHeading"],
        DebugAbilitySlotGroup.Generated => Text[Redesigned ? "Redesign.Research.Generated" : "Debug.Abilities.GeneratedSlotsHeading"],
        DebugAbilitySlotGroup.FinalFusion => Text[Redesigned ? "Redesign.Research.FinalFusionSlots" : "Debug.Abilities.FinalFusionSlotsHeading"],
        DebugAbilitySlotGroup.BodyGenerated => Text[Redesigned ? "Redesign.Research.BodyGenerated" : "Debug.Abilities.BodyGeneratedHeading"],
        DebugAbilitySlotGroup.HeadGenerated => Text[Redesigned ? "Redesign.Research.HeadGenerated" : "Debug.Abilities.HeadGeneratedHeading"],
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
