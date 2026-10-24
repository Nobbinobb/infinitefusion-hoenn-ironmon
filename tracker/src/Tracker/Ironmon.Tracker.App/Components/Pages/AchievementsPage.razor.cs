using System.Globalization;
using Ironmon.Tracker.Collection;
using Ironmon.Tracker.Collection.Achievements;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Ironmon.Tracker.App.Components.Pages;

/// <summary>
/// Presents earned achievements without revealing locked metadata or artwork.
/// </summary>
public partial class AchievementsPage : IAsyncDisposable
{
    private static readonly int _totalAchievements = GameplayCollectionStore.AchievementCount;
    private const string _unknownCount = "—";
    private const string _newestSort = "newest";
    private const string _oldestSort = "oldest";
    private const string _nameSort = "name";
    private const string _escapeKey = "Escape";
    private const string _timestampFormat = "O";
    private const string _fullDateFormat = "f";
    private const string _shortDateFormat = "d MMM yyyy";
    private const string _timeFormat = "t";
    private const string _percentageFormat = "0.###";
    private const string _progressStyleFormat = "width:{0}%";
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(2));
    private readonly Dictionary<Guid, string> _icons = [];
    private readonly HashSet<Guid> _artworkIds = [];
    private UnlockedAchievement? _artworkAchievement;
    private IReadOnlyList<UnlockedAchievement> _earned = [];
    private IReadOnlyList<UnlockedAchievement> _visible = [];
    private string _query = string.Empty;
    private string _sort = _newestSort;
    private Task? _refresh;
    private bool _hasSnapshot;
    private bool _loading = true;
    private bool _refreshing;
    private bool _unavailable;

    /// <summary>
    /// Gets or initializes the shared durable collection and achievement service.
    /// </summary>
    [Inject]
    private GameplayCollectionStore Collection { get; set; } = null!;

    /// <summary>
    /// Gets or sets the action returning to the previously selected tracker view.
    /// </summary>
    [Parameter]
    public EventCallback BackRequested { get; set; }

    /// <summary>
    /// Gets the invariant CSS width representing the earned fraction of the current achievement pack.
    /// </summary>
    private string ProgressStyle
    {
        get
        {
            string percentage = (_earned.Count * 100d / _totalAchievements).ToString(_percentageFormat, CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, _progressStyleFormat, percentage);
        }
    }

    /// <summary>
    /// Loads only revealed records and starts a bounded refresh loop.
    /// </summary>
    /// <returns>A task completing after the initial earned list has loaded.</returns>
    protected override async Task OnInitializedAsync()
    {
        await RefreshAsync();
        _refresh = PollAsync();
    }

    /// <summary>
    /// Refreshes earned records while preserving the current query, sort and last successful snapshot.
    /// </summary>
    /// <returns>A task completing after the visible list or availability state updates.</returns>
    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        try
        {
            _earned = await Collection.GetAchievementsAsync(cancellationToken: _lifetime.Token);
            UpdateVisibleAchievements();
            Guid[] missingIcons = [.. _earned.Where(item => !_icons.ContainsKey(item.Id)).Select(item => item.Id)];
            if (missingIcons.Length > 0)
            {
                var icons = await Collection.GetAchievementIconsAsync(missingIcons, _lifetime.Token);
                foreach (var pair in icons)
                {
                    _icons[pair.Key] = pair.Value.ImageDataUri;
                    if (pair.Value.HasArtwork)
                        _artworkIds.Add(pair.Key);
                }
            }

            _hasSnapshot = true;
            _unavailable = false;
            UpdateVisibleAchievements();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            _unavailable = true;
        }
        finally
        {
            _loading = false;
            _refreshing = false;
        }
    }

    /// <summary>
    /// Applies whitespace-separated search terms only to already revealed names and conditions.
    /// </summary>
    private void UpdateVisibleAchievements()
    {
        string[] terms = _query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<UnlockedAchievement> matches = _earned.Where(achievement => terms.All(term => achievement.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) || achievement.Condition.Contains(term, StringComparison.CurrentCultureIgnoreCase)));
        IOrderedEnumerable<UnlockedAchievement> ordered = _sort switch
        {
            _oldestSort => matches.OrderBy(achievement => achievement.UnlockedAt),
            _nameSort => matches.OrderBy(achievement => achievement.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => matches.OrderByDescending(achievement => achievement.UnlockedAt)
        };

        _visible = [.. ordered.ThenBy(achievement => achievement.Number)];
    }

    /// <summary>
    /// Clears the search without changing the selected order or collection total.
    /// </summary>
    private void ClearSearch()
    {
        _query = string.Empty;
        UpdateVisibleAchievements();
    }

    /// <summary>
    /// Selects an earned icon with final detail artwork for the shared dialog frame.
    /// </summary>
    /// <param name="achievement">The revealed collection row selected by the user.</param>
    private void OpenArtwork(UnlockedAchievement achievement)
    {
        if (_artworkIds.Contains(achievement.Id) && _earned.Any(item => item.Id == achievement.Id))
            _artworkAchievement = achievement;
    }

    /// <summary>
    /// Removes the viewer so its component releases the selected image and restores focus.
    /// </summary>
    private void CloseArtwork()
        => _artworkAchievement = null;

    /// <summary>
    /// Clears a focused search when Escape is pressed without invoking tracker shortcuts.
    /// </summary>
    /// <param name="args">The search field's keyboard event.</param>
    private void HandleSearchKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == _escapeKey)
            ClearSearch();
    }

    /// <summary>
    /// Retries an initial failure without exposing a false empty collection.
    /// </summary>
    /// <returns>A task completing after the new read attempt finishes.</returns>
    private async Task RetryAsync()
    {
        _loading = true;
        _unavailable = false;
        await RefreshAsync();
    }

    /// <summary>
    /// Formats one unlock in local time using a short relative day or a complete calendar date.
    /// </summary>
    /// <param name="unlockedAt">The original durable unlock time in UTC.</param>
    /// <returns>Today's localized time, yesterday, or the localized calendar date.</returns>
    private string FormatUnlockDate(DateTimeOffset unlockedAt)
    {
        DateTimeOffset local = unlockedAt.ToLocalTime();
        DateTime today = DateTime.Today;
        if (local.Date == today)
            return Text[AchievementText.Today, local.ToString(_timeFormat, CultureInfo.CurrentCulture)];

        return local.Date == today.AddDays(-1) ? Text[AchievementText.Yesterday] : local.ToString(_shortDateFormat, CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a machine-readable timestamp without changing its durable unlock instant.
    /// </summary>
    /// <param name="unlockedAt">The original durable unlock time.</param>
    /// <returns>The invariant round-trip timestamp for the HTML time element.</returns>
    private static string FormatTimestamp(DateTimeOffset unlockedAt) => unlockedAt.ToString(_timestampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Polls the single writer queue without retaining a subscription after navigation.
    /// </summary>
    /// <returns>A task completing when this page is disposed.</returns>
    private async Task PollAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_lifetime.Token))
            {
                await InvokeAsync(async () =>
                {
                    await RefreshAsync();
                    StateHasChanged();
                });
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }

    /// <summary>
    /// Stops and drains the refresh loop when returning to the tracker.
    /// </summary>
    /// <returns>A value task completing after timer and cancellation resources are released.</returns>
    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _timer.Dispose();
        if (_refresh is not null)
            await _refresh;

        _lifetime.Dispose();
    }
}
