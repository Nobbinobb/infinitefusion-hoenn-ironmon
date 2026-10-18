using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Player;

/// <summary>
/// Displays categorized battle items and sends battle-action selections to the game.
/// </summary>
public partial class BattleItemPanel
{
    private static readonly BattleItemCategory[] _categories = Enum.GetValues<BattleItemCategory>();
    private BattleItemCategory _selectedCategory;
    private bool _busy;
    private string? _status;

    /// <summary>
    /// Gets or initializes the connected-game request client.
    /// </summary>
    [Inject]
    private TrackerRequestClient Requests { get; set; } = null!;

    /// <summary>
    /// Gets or sets the current categorized battle-item inventory.
    /// </summary>
    [Parameter]
    public HealingInventorySnapshot Inventory { get; set; } = null!;

    /// <summary>
    /// Gets or sets the active Pokemon's moves for PP-item targeting.
    /// </summary>
    [Parameter]
    public IReadOnlyList<PlayerMoveSnapshot> Moves { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the game currently has an active battle.
    /// </summary>
    [Parameter]
    public bool InBattle { get; set; }

    /// <summary>
    /// Gets or sets the active battle identifier.
    /// </summary>
    [Parameter]
    public string? BattleId { get; set; }

    /// <summary>
    /// Gets or sets the selected opposing battler position.
    /// </summary>
    [Parameter]
    public int? TargetPosition { get; set; }

    /// <summary>
    /// Gets or sets the callback raised when the panel should close.
    /// </summary>
    [Parameter]
    public EventCallback Closed { get; set; }

    /// <summary>
    /// Gets whether item requests can currently be submitted.
    /// </summary>
    private bool CanUseItems => InBattle && BattleId is not null && !_busy;

    /// <summary>
    /// Closes the item panel.
    /// </summary>
    /// <returns>A task representing callback delivery.</returns>
    private Task Close()
        => Closed.InvokeAsync();

    /// <summary>
    /// Selects one inventory category.
    /// </summary>
    /// <param name="category">The selected category.</param>
    private void SelectCategory(BattleItemCategory category)
    {
        _selectedCategory = category;
        _status = null;
    }

    /// <summary>
    /// Gets the item stacks in the selected category.
    /// </summary>
    /// <returns>The selected category's item stacks.</returns>
    private IReadOnlyList<BattleItemSnapshot> GetSelectedItems()
        => [.. Inventory.Items.Where(item => item.Category == _selectedCategory)];

    /// <summary>
    /// Gets the total item quantity in one category.
    /// </summary>
    /// <param name="category">The category to total.</param>
    /// <returns>The total carried quantity.</returns>
    private int GetItemCount(BattleItemCategory category)
        => Inventory.Items.Where(item => item.Category == category).Sum(item => item.Quantity);

    /// <summary>
    /// Gets the localized name for one item category.
    /// </summary>
    /// <param name="category">The category to name.</param>
    /// <returns>The localized category name.</returns>
    private string GetCategoryName(BattleItemCategory category) => category switch
    {
        BattleItemCategory.Healing => Text["Player.Items.Healing"],
        BattleItemCategory.PpRestore => Text["Player.Items.PpRestore"],
        BattleItemCategory.Status => Text["Player.Items.Status"],
        BattleItemCategory.CombatStat => Text["Player.Items.CombatStat"],
        BattleItemCategory.Other => Text["Player.Items.Other"],
        _ => throw new ArgumentOutOfRangeException(nameof(category), category, null)
    };

    /// <summary>
    /// Gets the visual state for one category tab.
    /// </summary>
    /// <param name="category">The category tab.</param>
    /// <returns>The tab CSS classes.</returns>
    private string GetCategoryClass(BattleItemCategory category)
        => category == _selectedCategory ? "selected" : string.Empty;

    /// <summary>
    /// Sends one authoritative item-action request to the connected game.
    /// </summary>
    /// <param name="item">The selected item stack.</param>
    /// <param name="moveIndex">The selected move index when required.</param>
    /// <returns>A task representing request delivery.</returns>
    private async Task UseItem(BattleItemSnapshot item, int? moveIndex)
    {
        if (!CanUseItems)
            return;

        _busy = true;
        _status = null;
        try
        {
            BattleItemUseRequestPayload request = new() { ItemId = item.Id, MoveIndex = moveIndex, TargetPosition = TargetPosition };
            BattleItemUseResponsePayload response = await Requests.UseBattleItemAsync(request, BattleId!);
            if (response.Accepted)
            {
                await Close();
                return;
            }

            _status = response.Message;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or TrackerProtocolException or TimeoutException)
        {
            _status = Text["Player.Items.RequestFailed"];
        }
        finally
        {
            _busy = false;
        }
    }
}
