namespace Ironmon.Tracker.App.Controls;

/// <summary>
/// Matches alternatives and gives longer chords priority over their component controls.
/// </summary>
public static class ControlMatcher
{
    /// <summary>
    /// Resolves pressed actions while consuming shorter chords and suppressing ambiguous exact matches.
    /// </summary>
    /// <typeparam name="T">The physical control type.</typeparam>
    /// <param name="bindings">Alternative physical chords by action.</param>
    /// <param name="pressed">The currently held physical controls.</param>
    /// <returns>The matched action identifiers.</returns>
    public static HashSet<string> Match<T>(IReadOnlyDictionary<string, T[][]> bindings, IReadOnlySet<T> pressed) where T : notnull
    {
        (string Action, HashSet<T> Chord)[] matches = [.. bindings.SelectMany(entry => entry.Value
            .Where(chord => chord.Length > 0 && chord.All(pressed.Contains))
            .Select(chord => (Action: entry.Key, Chord: chord.ToHashSet())))];

        (string Action, HashSet<T> Chord)[] preferred = [.. matches.Where(match => !matches.Any(other => match.Action != other.Action && ControlCatalog.ShareInputContext(match.Action, other.Action) && match.Chord.IsProperSubsetOf(other.Chord)))];

        return preferred.Where(match => !preferred.Any(other => match.Action != other.Action && ControlCatalog.ShareInputContext(match.Action, other.Action) && match.Chord.SetEquals(other.Chord)))
            .Select(match => match.Action).ToHashSet(StringComparer.Ordinal);
    }
}
