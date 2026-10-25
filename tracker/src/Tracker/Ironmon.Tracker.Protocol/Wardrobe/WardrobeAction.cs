namespace Ironmon.Tracker.Protocol.Wardrobe;

/// <summary>
/// Defines allowed operations on an existing wardrobe; opening is exclusively game-owned.
/// </summary>
public enum WardrobeAction
{
    /// <summary>
    /// Rejects a missing operation.
    /// </summary>
    None = 0,

    /// <summary>
    /// Replaces the preview appearance.
    /// </summary>
    Change = 1,

    /// <summary>
    /// Permanently unlocks a selected item.
    /// </summary>
    Purchase = 2,

    /// <summary>
    /// Applies the draft and returns to the game.
    /// </summary>
    Confirm = 3,

    /// <summary>
    /// Discards the draft and returns to the game.
    /// </summary>
    Cancel = 4,

    /// <summary>
    /// Creates a named shared outfit.
    /// </summary>
    SaveProfile = 5,

    /// <summary>
    /// Explicitly replaces a named outfit.
    /// </summary>
    ReplaceProfile = 6,

    /// <summary>
    /// Renames a saved outfit.
    /// </summary>
    RenameProfile = 7,

    /// <summary>
    /// Deletes a saved outfit without changing ownership.
    /// </summary>
    DeleteProfile = 8,

    /// <summary>
    /// Loads a saved outfit into the draft.
    /// </summary>
    LoadProfile = 9,
}
