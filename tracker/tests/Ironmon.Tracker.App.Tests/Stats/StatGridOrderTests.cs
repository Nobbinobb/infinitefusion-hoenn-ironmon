using Ironmon.Tracker.App.Components.Enemy;
using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Player;
using Ironmon.Tracker.Connection.Knowledge;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace Ironmon.Tracker.App.Tests.Stats;

/// <summary>
/// Verifies the shared player and enemy stat-row presentation order.
/// </summary>
public sealed class StatGridOrderTests
{
    private const string _attackLabel = "ATK";
    private const string _defenseLabel = "DEF";
    private const string _hpLabel = "HP";
    private const string _labelPrefix = ">";
    private const string _labelSuffix = "</span>";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _rootName = "IronmonStatGridTests";
    private const string _specialAttackLabel = "SPA";
    private const string _specialDefenseLabel = "SPD";
    private const string _speedLabel = "SPE";
    private const string _bstLabel = "BST";
    private static readonly string[] _expectedOrder =
        [_hpLabel, _specialAttackLabel, _specialDefenseLabel, _attackLabel, _defenseLabel, _speedLabel, _bstLabel];

    /// <summary>
    /// Verifies both cards render head contributions followed by body contributions and BST.
    /// </summary>
    /// <param name="componentType">The production stat-grid component to render.</param>
    [Theory]
    [InlineData(typeof(PlayerCard))]
    [InlineData(typeof(EnemyStatNotes))]
    public async Task StatGridGroupsBodyAndHeadContributions(Type componentType)
    {
        string root = Path.Combine(Path.GetTempPath(), _rootName, Guid.NewGuid().ToString());
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new StatGridLocalizer());
        services.AddSingleton(new TrackerKnowledgeStore(new TrackerKnowledgeOptions(root)));
        services.AddSingleton(new PokemonSpriteDialogService());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync(componentType, ParameterView.Empty);
            AssertStatOrder(view.ToHtmlString());
        });
    }

    /// <summary>
    /// Verifies each stat label follows the requested row-major order.
    /// </summary>
    /// <param name="html">The rendered production component HTML.</param>
    private static void AssertStatOrder(string html)
    {
        int previousIndex = -1;
        foreach (string label in _expectedOrder)
        {
            int index = html.IndexOf($"{_labelPrefix}{label}{_labelSuffix}", previousIndex + 1, StringComparison.Ordinal);
            Assert.True(index > previousIndex, $"Stat label {label} was not rendered in the expected body/head order.");
            previousIndex = index;
        }
    }

    /// <summary>
    /// Resolves production tracker text for component rendering.
    /// </summary>
    private sealed class StatGridLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly System.Resources.ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the production text for one resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Formats production text with supplied values.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns an empty catalog because tests request individual keys.
        /// </summary>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
