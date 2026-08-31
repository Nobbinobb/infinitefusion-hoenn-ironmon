namespace Ironmon.Tracker.Connection.Areas;

/// <summary>
/// Compares complete area discovery entries by their persisted protocol fields.
/// </summary>
internal static class AreaDiscoveryEntryComparer
{
    /// <summary>
    /// Determines whether two trainer entries contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing entry.</param>
    /// <param name="right">The new entry.</param>
    /// <returns>True when all persisted fields match.</returns>
    internal static bool AreEquivalent(AreaTrainerEntryPayload left, AreaTrainerEntryPayload right)
    {
        if (left.EntryId != right.EntryId
            || left.MapId != right.MapId
            || left.TrainerType != right.TrainerType
            || left.TrainerName != right.TrainerName
            || left.PartySize != right.PartySize
            || left.Defeated != right.Defeated
            || left.DetailsRevealed != right.DetailsRevealed
            || left.Party.Count != right.Party.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Party.Count; index++)
        {
            if (!AreEquivalent(left.Party[index], right.Party[index]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether two encounter entries contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing entry.</param>
    /// <param name="right">The new entry.</param>
    /// <returns>True when all persisted fields match.</returns>
    internal static bool AreEquivalent(AreaEncounterEntryPayload left, AreaEncounterEntryPayload right)
    {
        return left.EntryId == right.EntryId
            && left.MapId == right.MapId
            && left.Version == right.Version
            && left.EncounterType == right.EncounterType
            && left.Environment == right.Environment
            && left.Slot == right.Slot
            && left.ProbabilityPercent == right.ProbabilityPercent
            && left.MinimumLevel == right.MinimumLevel
            && left.MaximumLevel == right.MaximumLevel
            && left.Encountered == right.Encountered
            && left.DetailsRevealed == right.DetailsRevealed
            && left.SpeciesId == right.SpeciesId
            && left.SpeciesName == right.SpeciesName
            && left.SpritePath == right.SpritePath
            && left.IndependentFusion == right.IndependentFusion;
    }

    /// <summary>
    /// Determines whether two item entries contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing entry.</param>
    /// <param name="right">The new entry.</param>
    /// <returns>True when all persisted fields match.</returns>
    internal static bool AreEquivalent(AreaItemEntryPayload left, AreaItemEntryPayload right)
    {
        if (left.EntryId != right.EntryId
            || left.MapId != right.MapId
            || left.X != right.X
            || left.Y != right.Y
            || left.Kind != right.Kind
            || left.Hidden != right.Hidden
            || left.Collected != right.Collected
            || left.DetailsRevealed != right.DetailsRevealed
            || left.Items.Count != right.Items.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Items.Count; index++)
        {
            if (!AreEquivalent(left.Items[index], right.Items[index]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether two trainer Pokemon contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing Pokemon.</param>
    /// <param name="right">The new Pokemon.</param>
    /// <returns>True when all persisted fields match.</returns>
    private static bool AreEquivalent(AreaTrainerPokemonPayload left, AreaTrainerPokemonPayload right)
    {
        return left.Slot == right.Slot
            && left.SpeciesId == right.SpeciesId
            && left.SpeciesName == right.SpeciesName
            && left.Level == right.Level
            && left.SpritePath == right.SpritePath;
    }

    /// <summary>
    /// Determines whether two item identities contain identical persisted data.
    /// </summary>
    /// <param name="left">The existing identity.</param>
    /// <param name="right">The new identity.</param>
    /// <returns>True when all persisted fields match.</returns>
    private static bool AreEquivalent(AreaItemIdentityPayload left, AreaItemIdentityPayload right)
        => left.ItemId == right.ItemId && left.ItemName == right.ItemName;
}
