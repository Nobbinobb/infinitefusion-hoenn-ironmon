namespace Ironmon.Tracker.Connection.Knowledge;

/// <summary>
/// Compares retained knowledge fields and locally completed move presentations when deciding whether to refresh an observation.
/// </summary>
internal static class TrackerKnowledgeSnapshotComparer
{
    /// <summary>
    /// Determines whether an incoming move observation changes retained discovery data.
    /// </summary>
    /// <remarks>
    /// Presentation values participate only when the incoming observation carries local completion provenance.
    /// Ordinary native presentations retain the existing comparison behavior and do not trigger a refresh by themselves.
    /// </remarks>
    /// <param name="left">The existing observation.</param>
    /// <param name="right">The new observation.</param>
    /// <returns><see langword="true"/> when the retained metadata and any applicable completed presentation match; otherwise, <see langword="false"/>.</returns>
    internal static bool AreEquivalent(ObservedMoveSnapshot left, ObservedMoveSnapshot right)
    {
        return left.Id == right.Id
            && left.Name == right.Name
            && left.LearnedLevel == right.LearnedLevel
            && left.LearnOrder == right.LearnOrder
            && left.Source == right.Source
            && left.Origin == right.Origin
            && left.Type == right.Type
            && left.Category == right.Category
            && left.Description == right.Description
            && left.Power == right.Power
            && left.PowerPresentationDeferred == right.PowerPresentationDeferred
            && (!right.CompletedDeferredPowerPresentation || AreEquivalentPower(left.PowerPresentation, right.PowerPresentation))
            && left.Accuracy == right.Accuracy
            && left.TotalPp == right.TotalPp
            && left.PpAfterUse == right.PpAfterUse;
    }

    /// <summary>
    /// Compares completed stateless displays when refreshing a deferred observation.
    /// </summary>
    /// <param name="left">The existing explicit display.</param>
    /// <param name="right">The newly completed display.</param>
    /// <returns><see langword="true"/> when both presentations are absent or their display fields and ordered outcomes match; otherwise, <see langword="false"/>.</returns>
    private static bool AreEquivalentPower(MovePowerPresentationSnapshot? left, MovePowerPresentationSnapshot? right)
    {
        if (ReferenceEquals(left, right))
            return true;

        if (left is null || right is null)
            return false;

        return left.Display == right.Display
            && left.Indicator == right.Indicator
            && left.DetailsKind == right.DetailsKind
            && left.AccuracyCheckedPerHit == right.AccuracyCheckedPerHit
            && left.Outcomes.Count == right.Outcomes.Count
            && left.Outcomes.Zip(right.Outcomes).All(pair => pair.First.Kind == pair.Second.Kind
                && pair.First.Power == pair.Second.Power && pair.First.Hits == pair.Second.Hits
                && pair.First.ChancePercent == pair.Second.ChancePercent
                && pair.First.Minimum == pair.Second.Minimum && pair.First.Maximum == pair.Second.Maximum
                && pair.First.HealingPercent == pair.Second.HealingPercent);
    }

    /// <summary>
    /// Determines whether two abilities contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing ability.</param>
    /// <param name="right">The new ability.</param>
    /// <returns><see langword="true"/> when the ability identifier, name, and description match; otherwise, <see langword="false"/>.</returns>
    internal static bool AreEquivalent(AbilitySnapshot left, AbilitySnapshot right)
        => left.Id == right.Id && left.Name == right.Name && left.Description == right.Description;
}
