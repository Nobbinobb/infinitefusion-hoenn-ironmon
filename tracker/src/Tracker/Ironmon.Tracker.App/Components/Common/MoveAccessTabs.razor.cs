using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Components.Common;

/// <summary>
/// Renders shared Learnset, Egg, TM, and Tutor move-access tabs.
/// </summary>
public partial class MoveAccessTabs
{
    private const int _pageSize = 15;
    private const string _scrollPage = "ironmonTrackerUi.scrollMoveAccess";
    private readonly PaginationState _pagination = new(_pageSize);
    private ElementReference _element;
    private bool _scrollToTop;
    private string _selectedTab = MoveAccessTabIds.Learnset;
    private MoveAccessEntrySnapshot? _selectedMove;

    /// <summary>
    /// Gets the browser runtime used to reveal the start of each move page.
    /// </summary>
    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    /// <summary>
    /// Gets or sets the generated move-access channels.
    /// </summary>
    [Parameter]
    public MoveAccessSnapshot Access { get; set; } = new();

    /// <summary>
    /// Keeps the selected page within updated move-access bounds.
    /// </summary>
    protected override void OnParametersSet()
        => _pagination.Clamp(GetRows().Count);

    /// <summary>
    /// Reveals the first move after a page or category change.
    /// </summary>
    /// <param name="firstRender">Whether this is the initial render.</param>
    /// <returns>The content scrolling task.</returns>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_scrollToTop)
            return;

        _scrollToTop = false;
        await JavaScript.InvokeVoidAsync(_scrollPage, _element);
    }

    /// <summary>
    /// Opens the preceding page of moves.
    /// </summary>
    private void PreviousPage()
    {
        _pagination.Previous();
        _selectedMove = null;
        _scrollToTop = true;
    }

    /// <summary>
    /// Opens the following page without passing the final move.
    /// </summary>
    private void NextPage()
    {
        _pagination.Next(GetRows().Count);
        _selectedMove = null;
        _scrollToTop = true;
    }

    /// <summary>
    /// Selects one move-access channel.
    /// </summary>
    /// <param name="tab">The selected channel identifier.</param>
    private void SelectTab(string tab)
    {
        _selectedTab = tab;
        _selectedMove = null;
        _pagination.Reset();
        _scrollToTop = true;
    }

    /// <summary>
    /// Gets the CSS class for one channel tab.
    /// </summary>
    /// <param name="tab">The represented channel identifier.</param>
    /// <returns>The tab CSS classes.</returns>
    private string GetTabClass(string tab)
        => _selectedTab == tab ? TrackerUiConstants.SelectedCssClass : string.Empty;

    /// <summary>
    /// Gets the rows for the selected channel.
    /// </summary>
    /// <returns>The selected move-access rows.</returns>
    private IReadOnlyList<MoveAccessEntrySnapshot> GetRows() => _selectedTab switch
    {
        MoveAccessTabIds.Egg => Access.EggMoves,
        MoveAccessTabIds.Machine => Access.MachineMoves,
        MoveAccessTabIds.Tutor => Access.TutorMoves,
        _ => Access.Learnset
    };

    /// <summary>
    /// Gets the heading for the selected channel.
    /// </summary>
    /// <returns>The visible channel heading.</returns>
    private string GetHeading() => _selectedTab switch
    {
        MoveAccessTabIds.Egg => Text["Common.MoveAccess.EggMovesHeading"],
        MoveAccessTabIds.Machine => Text["Common.MoveAccess.TmTrCompatibilityHeading"],
        MoveAccessTabIds.Tutor => Text["Common.MoveAccess.SupportedTutorMovesHeading"],
        _ => Text["Common.MoveAccess.LevelUpLearnsetHeading"]
    };

    /// <summary>
    /// Gets the acquisition label shown before one move.
    /// </summary>
    /// <param name="move">The represented move row.</param>
    /// <returns>The primary acquisition label.</returns>
    private string GetPrimaryLabel(MoveAccessEntrySnapshot move)
    {
        if (move.LearnedLevel is not null)
            return move.LearnedLevel == MoveAccessConstants.EvolutionLearnedLevel ? Text["Common.MoveAccess.Evolution"] : Text["Common.MoveAccess.LevelValue", move.LearnedLevel];

        if (!string.IsNullOrWhiteSpace(move.ItemName))
            return move.ItemName;

        if (!string.IsNullOrWhiteSpace(move.TutorName))
            return move.TutorName;

        return Text["Common.MoveAccess.EggMove"];
    }

    /// <summary>
    /// Opens full information for one generated move.
    /// </summary>
    /// <param name="move">The selected move.</param>
    private void SelectMove(MoveAccessEntrySnapshot move)
        => _selectedMove = move;

    /// <summary>
    /// Closes the generated move information.
    /// </summary>
    private void CloseMove()
        => _selectedMove = null;
}
