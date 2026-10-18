using System.Text.Json;

namespace Ironmon.Tracker.App.Controls;

/// <summary>
/// Persists keyboard bindings and separate profiles for controller hardware identities.
/// </summary>
public sealed class ControlProfiles
{
    private const string _preferenceKey = "tracker.controls.v1";
    private readonly Lock _sync = new();
    private readonly Action<string> _write;
    private ControlProfileDocument _document;

    /// <summary>
    /// Initializes the production preference store.
    /// </summary>
    public ControlProfiles() : this(() => Preferences.Default.Get(_preferenceKey, string.Empty), value => Preferences.Default.Set(_preferenceKey, value))
    {
    }

    /// <summary>
    /// Initializes a store with an injectable persistence boundary.
    /// </summary>
    /// <param name="read">Reads the serialized document.</param>
    /// <param name="write">Saves the serialized document.</param>
    public ControlProfiles(Func<string?> read, Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(write);
        _write = write;

        try
        {
            _document = JsonSerializer.Deserialize<ControlProfileDocument>(read() ?? string.Empty) ?? new();
            if (!IsValid(_document))
                _document = new();

            if (_document.SchemaVersion < 2)
            {
                ExpandLegacyBindings(_document.Keyboard);
                foreach (ControllerProfile profile in _document.Controllers.Values)
                    ExpandLegacyBindings(profile.Bindings);
            }

            _document.SchemaVersion = 2;
        }
        catch (JsonException)
        {
            _document = new() { SchemaVersion = 2 };
        }
    }

    /// <summary>
    /// Gets the saved profiles, including currently disconnected controllers.
    /// </summary>
    public IReadOnlyList<ControllerProfile> Controllers
    {
        get
        {
            lock (_sync)
                return [.. _document.Controllers.Values];
        }
    }

    /// <summary>
    /// Gets keyboard alternatives, falling back to the catalog for an unedited action.
    /// </summary>
    /// <param name="action">The action whose keyboard bindings are requested.</param>
    /// <returns>The saved alternatives or the action's defaults.</returns>
    public int[][] Keyboard(ControlAction action)
    {
        lock (_sync)
            return [.. (_document.Keyboard.GetValueOrDefault(action.Id) ?? action.Keyboard).Take(2)];
    }

    /// <summary>
    /// Gets bindings belonging only to the specified controller identity.
    /// </summary>
    /// <param name="action">The action whose controller bindings are requested.</param>
    /// <param name="identity">The controller's persistent identity.</param>
    /// <param name="mapped">Whether the controller has a standard SDL mapping.</param>
    /// <returns>The saved alternatives, mapped defaults, or an empty set for an unbound generic device.</returns>
    public string[][] Controller(ControlAction action, string identity, bool mapped)
    {
        lock (_sync)
        {
            Dictionary<string, string[][]>? bindings = _document.Controllers.GetValueOrDefault(identity)?.Bindings;
            return [.. (bindings?.GetValueOrDefault(action.Id) ?? (mapped ? action.Controller : [])).Take(2)];
        }
    }

    /// <summary>
    /// Registers connected hardware without replacing a previously saved profile.
    /// </summary>
    /// <param name="identity">The controller's persistent identity.</param>
    /// <param name="name">The controller's display name.</param>
    /// <param name="mapped">Whether the controller has a standard SDL mapping.</param>
    public void Remember(string identity, string name, bool mapped)
    {
        lock (_sync)
        {
            if (_document.Controllers.TryGetValue(identity, out ControllerProfile? existing) && existing.Name == name && existing.Mapped == mapped)
                return;

            Update(document =>
            {
                if (document.Controllers.TryGetValue(identity, out ControllerProfile? profile))
                {
                    profile.Name = name;
                    profile.Mapped = mapped;
                }
                else
                {
                    document.Controllers[identity] = new() { Identity = identity, Name = name, Mapped = mapped };
                }
            });
        }
    }

    /// <summary>
    /// Saves all keyboard alternatives for an action.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <param name="bindings">The replacement keyboard chords.</param>
    public void SetKeyboard(string action, int[][] bindings)
    {
        lock (_sync)
            Update(document => document.Keyboard[action] = bindings);
    }

    /// <summary>
    /// Saves all alternatives in one controller's profile.
    /// </summary>
    /// <param name="identity">The saved controller's persistent identity.</param>
    /// <param name="action">The action identifier.</param>
    /// <param name="bindings">The replacement controller chords.</param>
    public void SetController(string identity, string action, string[][] bindings)
    {
        lock (_sync)
            Update(document => document.Controllers[identity].Bindings[action] = bindings);
    }

    /// <summary>
    /// Replaces one keyboard slot and atomically clears conflicting slots in the keyboard profile.
    /// </summary>
    /// <param name="action">The action identifier.</param>
    /// <param name="slot">The zero-based binding slot, either zero or one.</param>
    /// <param name="chord">The replacement chord, or an empty chord to clear the slot.</param>
    /// <returns>The old bindings unbound by this assignment.</returns>
    public IReadOnlyList<ControlBindingSlot> SetKeyboardSlot(string action, int slot, int[] chord)
    {
        lock (_sync)
        {
            Dictionary<string, int[][]> bindings = ControlCatalog.Actions.ToDictionary(item => item.Id, Keyboard);
            IReadOnlyList<ControlBindingSlot> conflicts = FindConflicts(bindings, action, chord);
            Update(document => AssignSlot(document.Keyboard, bindings, action, slot, chord, conflicts));
            return conflicts;
        }
    }

    /// <summary>
    /// Replaces one controller slot and atomically clears conflicting slots only in that device's profile.
    /// </summary>
    /// <param name="identity">The saved controller's identity.</param>
    /// <param name="action">The action identifier.</param>
    /// <param name="slot">The zero-based binding slot, either zero or one.</param>
    /// <param name="chord">The replacement chord, or an empty chord to clear the slot.</param>
    /// <returns>The old bindings unbound by this assignment.</returns>
    public IReadOnlyList<ControlBindingSlot> SetControllerSlot(string identity, string action, int slot, string[] chord)
    {
        lock (_sync)
        {
            ControllerProfile profile = _document.Controllers[identity];
            Dictionary<string, string[][]> bindings = ControlCatalog.Actions.ToDictionary(item => item.Id, item => Controller(item, identity, profile.Mapped));
            IReadOnlyList<ControlBindingSlot> conflicts = FindConflicts(bindings, action, chord);
            Update(document => AssignSlot(document.Controllers[identity].Bindings, bindings, action, slot, chord, conflicts));
            return conflicts;
        }
    }

    /// <summary>
    /// Finds old bindings that a proposed keyboard assignment would unbind.
    /// </summary>
    /// <param name="action">The action receiving the new binding.</param>
    /// <param name="chord">The proposed keyboard chord.</param>
    /// <returns>Conflicting slots in the keyboard profile.</returns>
    public IReadOnlyList<ControlBindingSlot> KeyboardConflicts(string action, int[] chord)
    {
        lock (_sync)
            return FindConflicts(ControlCatalog.Actions.ToDictionary(item => item.Id, Keyboard), action, chord);
    }

    /// <summary>
    /// Finds old bindings that a proposed controller assignment would unbind.
    /// </summary>
    /// <param name="identity">The selected controller's identity.</param>
    /// <param name="action">The action receiving the new binding.</param>
    /// <param name="chord">The proposed controller chord.</param>
    /// <returns>Conflicting slots in the selected controller profile.</returns>
    public IReadOnlyList<ControlBindingSlot> ControllerConflicts(string identity, string action, string[] chord)
    {
        lock (_sync)
        {
            bool mapped = _document.Controllers[identity].Mapped;
            return FindConflicts(ControlCatalog.Actions.ToDictionary(item => item.Id, item => Controller(item, identity, mapped)), action, chord);
        }
    }

    /// <summary>
    /// Finds ambiguous bindings retained by profiles saved before conflict replacement was introduced.
    /// </summary>
    /// <param name="identity">The controller identity, or null for the keyboard profile.</param>
    /// <returns>Every slot involved in an overlapping exact-chord conflict.</returns>
    public IReadOnlyList<ControlBindingSlot> ConflictingSlots(string? identity)
    {
        lock (_sync)
        {
            if (identity is null)
                return FindAllConflicts(ControlCatalog.Actions.ToDictionary(item => item.Id, Keyboard));

            bool mapped = _document.Controllers[identity].Mapped;
            return FindAllConflicts(ControlCatalog.Actions.ToDictionary(item => item.Id, item => Controller(item, identity, mapped)));
        }
    }

    /// <summary>
    /// Compares physical input sets only for actions that can activate in the same context.
    /// </summary>
    /// <typeparam name="T">The physical input type.</typeparam>
    /// <param name="bindings">The profile's effective bindings, including defaults.</param>
    /// <param name="action">The action receiving a binding.</param>
    /// <param name="chord">The proposed physical inputs.</param>
    /// <returns>The conflicting old slots, excluding the receiving action.</returns>
    private static IReadOnlyList<ControlBindingSlot> FindConflicts<T>(IReadOnlyDictionary<string, T[][]> bindings, string action, T[] chord) where T : notnull
    {
        HashSet<T> inputs = [.. chord];
        if (inputs.Count == 0)
            return [];

        return [.. bindings.Where(entry => entry.Key != action && ControlCatalog.ShareInputContext(action, entry.Key))
            .SelectMany(entry => entry.Value.Select((oldChord, slot) => (oldChord, Binding: new ControlBindingSlot(entry.Key, slot))))
            .Where(entry => inputs.SetEquals(entry.oldChord)).Select(entry => entry.Binding)];
    }

    /// <summary>
    /// Collects distinct slots involved in an existing profile conflict.
    /// </summary>
    /// <typeparam name="T">The physical input type.</typeparam>
    /// <param name="bindings">The profile's effective bindings.</param>
    /// <returns>The slots whose exact chords overlap another action in the same context.</returns>
    private static IReadOnlyList<ControlBindingSlot> FindAllConflicts<T>(IReadOnlyDictionary<string, T[][]> bindings) where T : notnull
    {
        return [.. bindings.SelectMany(entry => entry.Value.SelectMany(chord => FindConflicts(bindings, entry.Key, chord)))
            .DistinctBy(binding => (binding.Action, binding.Slot))];
    }

    /// <summary>
    /// Applies the new assignment and clears all old conflicts in the same candidate document.
    /// </summary>
    /// <typeparam name="T">The physical input type.</typeparam>
    /// <param name="overrides">The candidate profile's overrides.</param>
    /// <param name="bindings">The effective bindings before the assignment.</param>
    /// <param name="action">The receiving action.</param>
    /// <param name="slot">The receiving slot.</param>
    /// <param name="chord">The new chord.</param>
    /// <param name="conflicts">The old slots to unbind.</param>
    private static void AssignSlot<T>(Dictionary<string, T[][]> overrides, IReadOnlyDictionary<string, T[][]> bindings, string action, int slot, T[] chord, IReadOnlyList<ControlBindingSlot> conflicts)
    {
        foreach (ControlBindingSlot conflict in conflicts)
            overrides[conflict.Action] = ReplaceSlot(overrides.GetValueOrDefault(conflict.Action) ?? bindings[conflict.Action], conflict.Slot, []);

        overrides[action] = ReplaceSlot(bindings[action], slot, chord);
    }

    /// <summary>
    /// Creates two positional slots and updates only the requested slot.
    /// </summary>
    /// <typeparam name="T">The physical input type.</typeparam>
    /// <param name="bindings">The previous alternatives.</param>
    /// <param name="slot">The slot to replace.</param>
    /// <param name="chord">The replacement physical inputs.</param>
    /// <returns>The two edited slots.</returns>
    private static T[][] ReplaceSlot<T>(T[][] bindings, int slot, T[] chord)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(slot, 1);
        T[][] result = [bindings.ElementAtOrDefault(0) ?? [], bindings.ElementAtOrDefault(1) ?? []];
        result[slot] = [.. chord];
        return result;
    }

    /// <summary>
    /// Copies legacy combined bindings once so subsequent contextual edits remain independent.
    /// </summary>
    /// <typeparam name="T">The physical input type.</typeparam>
    /// <param name="bindings">The persisted action alternatives to migrate in memory.</param>
    private static void ExpandLegacyBindings<T>(Dictionary<string, T[][]> bindings)
    {
        foreach (string action in new[] { ControlCatalog.Interact, ControlCatalog.Menu, ControlCatalog.BattleRun })
        {
            string legacy = action == ControlCatalog.Interact ? ControlCatalog.Confirm : ControlCatalog.Cancel;
            if (!bindings.ContainsKey(action) && bindings.TryGetValue(legacy, out T[][]? alternatives))
                bindings[action] = [.. alternatives.Select(chord => (T[])[.. chord])];
        }
    }

    /// <summary>
    /// Restores the selected keyboard or controller profile to its initial bindings.
    /// </summary>
    /// <param name="identity">The controller identity, or null for the keyboard profile.</param>
    public void Restore(string? identity)
    {
        lock (_sync)
        {
            Update(document =>
            {
                if (identity is null)
                {
                    document.Keyboard.Clear();
                }
                else if (document.Controllers.TryGetValue(identity, out ControllerProfile? profile))
                {
                    profile.Bindings.Clear();
                }
            });
        }
    }

    /// <summary>
    /// Commits a changed profile only after persistence succeeds.
    /// </summary>
    /// <param name="edit">The edit to apply to a candidate profile document.</param>
    private void Update(Action<ControlProfileDocument> edit)
    {
        ControlProfileDocument candidate = JsonSerializer.Deserialize<ControlProfileDocument>(JsonSerializer.Serialize(_document))!;
        edit(candidate);
        if (!IsValid(candidate))
            throw new ArgumentException("The control profile contains invalid bindings.");

        _write(JsonSerializer.Serialize(candidate));
        _document = candidate;
    }

    /// <summary>
    /// Rejects corrupt collections and unbounded or invalid physical bindings.
    /// </summary>
    /// <param name="document">The profile document to validate.</param>
    /// <returns>Whether all profiles and bindings satisfy the storage bounds.</returns>
    private static bool IsValid(ControlProfileDocument document)
    {
        if (document.Keyboard is null || document.Controllers is null || document.Controllers.Count > 128)
            return false;

        HashSet<string> actions = [.. ControlCatalog.Actions.Select(action => action.Id)];
        if (document.Keyboard.Any(entry => !actions.Contains(entry.Key) || entry.Value is null || entry.Value.Length > 16
            || entry.Value.Any(chord => chord is null || chord.Length > 32 || chord.Any(key => key < 8 || key >= 255))))
            return false;

        return document.Controllers.All(entry => entry.Value is { Bindings: not null } profile && profile.Identity == entry.Key
            && profile.Bindings.All(binding => actions.Contains(binding.Key) && binding.Value is not null && binding.Value.Length <= 16
                && binding.Value.All(chord => chord is not null && chord.Length <= 32 && chord.All(token => !string.IsNullOrWhiteSpace(token) && token.Length <= 64))));
    }
}

/// <summary>
/// Contains tracker-owned control preferences.
/// </summary>
public sealed class ControlProfileDocument
{
    /// <summary>
    /// Gets or sets the version distinguishing legacy combined bindings from independent actions.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets keyboard overrides by action.
    /// </summary>
    public Dictionary<string, int[][]> Keyboard { get; set; } = [];

    /// <summary>
    /// Gets or sets controller profiles by persistent identity.
    /// </summary>
    public Dictionary<string, ControllerProfile> Controllers { get; set; } = [];
}

/// <summary>
/// Contains the bindings remembered for one controller identity.
/// </summary>
public sealed class ControllerProfile
{
    /// <summary>
    /// Gets or sets the hardware identity.
    /// </summary>
    public string Identity { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the controller's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets whether SDL provides standard gamepad controls.
    /// </summary>
    public bool Mapped { get; set; }

    /// <summary>
    /// Gets or sets edited action bindings.
    /// </summary>
    public Dictionary<string, string[][]> Bindings { get; set; } = [];
}
