using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Starter;

/// <summary>
/// Presents legally revealed starter choices and the run's random pick.
/// </summary>
public partial class StarterSelection
{
    /// <summary>
    /// Gets or initializes the active starter-selection snapshot.
    /// </summary>
    [Parameter]
    public required StarterSelectionSnapshot Selection { get; set; }

    /// <summary>
    /// Gets or initializes the connected game installation directory.
    /// </summary>
    [Parameter]
    public string? GameRoot { get; set; }

    /// <summary>
    /// Gets the visual classes for one starter slot.
    /// </summary>
    /// <param name="choice">The represented starter slot.</param>
    /// <returns>The starter slot CSS classes.</returns>
    private string GetChoiceClass(StarterChoiceSnapshot choice)
    {
        List<string> classes = ["starter-choice"];
        classes.Add(choice.Revealed ? "revealed" : "hidden");
        if (Selection.RandomPickIndex == choice.Index)
            classes.Add("random-pick");

        if (choice.Revealed && choice.Favorite)
            classes.Add("favorite");

        return string.Join(' ', classes);
    }

    /// <summary>
    /// Gets an accessible description for one starter slot.
    /// </summary>
    /// <param name="choice">The represented starter slot.</param>
    /// <returns>The localized starter slot description.</returns>
    private string GetChoiceLabel(StarterChoiceSnapshot choice)
    {
        string identity = choice.Revealed
            ? Text["Starter.Selection.RevealedChoice", choice.SpeciesName ?? string.Empty, choice.BaseStatTotal.GetValueOrDefault()]
            : Text["Starter.Selection.HiddenChoice", choice.Index + 1];

        return Selection.RandomPickIndex == choice.Index
            ? Text["Starter.Selection.RandomPickChoice", identity]
            : identity;
    }

    /// <summary>
    /// Loads the revealed starter's local sprite.
    /// </summary>
    /// <param name="choice">The represented starter slot.</param>
    /// <returns>The WebView sprite source, or null when unavailable.</returns>
    private string? GetSpriteSource(StarterChoiceSnapshot choice)
        => LocalSpriteLoader.Load(GameRoot, choice.SpritePath);
}
