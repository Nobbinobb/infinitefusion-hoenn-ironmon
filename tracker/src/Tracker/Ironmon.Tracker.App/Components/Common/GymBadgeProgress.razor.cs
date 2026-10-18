using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Displays the save's eight gym badges without opening a detail dialog.
/// </summary>
public partial class GymBadgeProgress
{
    private const int _badgeCount = 8;
    private const int _spriteSize = 32;
    private const string _spritePath = "Graphics/Pictures/Trainer Card/icon_badges.png";
    private const string _earnedClass = "earned";
    private const string _unearnedClass = "unearned";
    private const string _unknownClass = "unknown";
    private const string _earnedSymbol = "✓";
    private const string _unearnedSymbol = "—";
    private const string _unknownSymbol = "?";
    private const string _earnedKey = "Badges.Earned";
    private const string _unearnedKey = "Badges.NotEarned";
    private const string _unknownKey = "Badges.Unknown";
    private const string _progressKey = "Badges.Progress";
    private const string _badgeLabelKey = "Badges.Label";
    private static readonly string[] _nameKeys = ["Badges.Stone", "Badges.Knuckle", "Badges.Dynamo", "Badges.Heat", "Badges.Balance", "Badges.Feather", "Badges.Mind", "Badges.Rain"];
    private string? _loadedGameRoot;
    private string? _spriteSource;

    /// <summary>
    /// Gets or sets the connected installation containing the trainer-card artwork.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets or sets individual badge ownership, with missing flags displayed as unknown.
    /// </summary>
    [Parameter]
    public IReadOnlyList<bool>? Badges { get; set; }

    /// <summary>
    /// Loads the badge sheet when the connected installation changes.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (_loadedGameRoot == GameRoot)
            return;

        _loadedGameRoot = GameRoot;
        _spriteSource = LocalSpriteLoader.Load(GameRoot, _spritePath);
    }

    /// <summary>
    /// Gets ownership for one slot without assuming badge acquisition order.
    /// </summary>
    /// <param name="index">The trainer-card badge index.</param>
    /// <returns>The reported flag, or null when unavailable.</returns>
    private bool? GetEarned(int index)
        => Badges is not null && index < Badges.Count ? Badges[index] : null;

    /// <summary>
    /// Gets the earned total only when all eight ownership flags are known.
    /// </summary>
    /// <returns>The earned count or an unknown marker.</returns>
    private string GetCountText()
        => Badges is { Count: >= _badgeCount } ? Badges.Take(_badgeCount).Count(earned => earned).ToString(CultureInfo.CurrentCulture) : _unearnedSymbol;

    /// <summary>
    /// Gets the accessible summary for the complete strip.
    /// </summary>
    /// <returns>The localized progress summary.</returns>
    private string GetProgressLabel()
        => Text[_progressKey, GetCountText(), _badgeCount];

    /// <summary>
    /// Gets the accessible name and ownership status of one badge.
    /// </summary>
    /// <param name="index">The trainer-card badge index.</param>
    /// <returns>The localized badge label.</returns>
    private string GetBadgeLabel(int index)
    {
        string statusKey = GetEarned(index) switch
        {
            true => _earnedKey,
            false => _unearnedKey,
            null => _unknownKey
        };

        return Text[_badgeLabelKey, Text[_nameKeys[index]], Text[statusKey]];
    }

    /// <summary>
    /// Gets the presentation class for one badge's ownership.
    /// </summary>
    /// <param name="index">The trainer-card badge index.</param>
    /// <returns>The ownership class.</returns>
    private string GetStatusClass(int index) => GetEarned(index) switch
    {
        true => _earnedClass,
        false => _unearnedClass,
        null => _unknownClass
    };

    /// <summary>
    /// Gets the ownership marker beneath one badge.
    /// </summary>
    /// <param name="index">The trainer-card badge index.</param>
    /// <returns>A checkmark, dash, or unknown marker.</returns>
    private string GetStatusSymbol(int index) => GetEarned(index) switch
    {
        true => _earnedSymbol,
        false => _unearnedSymbol,
        null => _unknownSymbol
    };

    /// <summary>
    /// Selects one 32-pixel cell from the original horizontal badge sheet.
    /// </summary>
    /// <param name="index">The trainer-card badge index.</param>
    /// <returns>The invariant CSS object position.</returns>
    private static string GetSpritePosition(int index)
        => string.Create(CultureInfo.InvariantCulture, $"object-position: {-index * _spriteSize}px 0");
}
