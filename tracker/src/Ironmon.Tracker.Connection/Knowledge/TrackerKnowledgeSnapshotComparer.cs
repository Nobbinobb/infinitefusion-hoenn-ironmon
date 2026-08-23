namespace Ironmon.Tracker.Connection.Knowledge;

/// <summary>
/// Compares tracker knowledge snapshots by their persisted protocol fields.
/// </summary>
internal static class TrackerKnowledgeSnapshotComparer
{
    /// <summary>
    /// Determines whether two move observations contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing observation.</param>
    /// <param name="right">The new observation.</param>
    /// <returns>True when all persisted fields match.</returns>
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
            && left.Accuracy == right.Accuracy
            && left.TotalPp == right.TotalPp
            && left.PpAfterUse == right.PpAfterUse;
    }

    /// <summary>
    /// Determines whether two abilities contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing ability.</param>
    /// <param name="right">The new ability.</param>
    /// <returns>True when all persisted fields match.</returns>
    internal static bool AreEquivalent(AbilitySnapshot left, AbilitySnapshot right)
        => left.Id == right.Id && left.Name == right.Name && left.Description == right.Description;
}
