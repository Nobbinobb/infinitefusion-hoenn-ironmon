using Ironmon.Tracker.App.Components.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Resources;

namespace Ironmon.Tracker.App.Tests.Stats;

/// <summary>
/// Verifies individual badge ownership and the display-only progress strip.
/// </summary>
public sealed class GymBadgeProgressTests
{
    private const string _earnedClass = "obsidian-badge earned";
    private const string _unknownClass = "obsidian-badge unknown";
    private const string _buttonTag = "<button";
    private const string _dialogRole = "role=\"dialog\"";

    /// <summary>
    /// Keeps out-of-order ownership distinct and provides no badge dialog action.
    /// </summary>
    [Fact]
    public async Task NonSequentialBadgesRetainTheirIdentity()
    {
        string html = await RenderAsync([true, false, true, false, false, false, false, true]);

        Assert.Equal(3, html.Split(_earnedClass).Length - 1);
        Assert.Contains("Stone Badge &#xB7; Earned", html);
        Assert.Contains("Knuckle Badge &#xB7; Not earned", html);
        Assert.Contains("Rain Badge &#xB7; Earned", html);
        Assert.DoesNotContain(_buttonTag, html);
        Assert.DoesNotContain(_dialogRole, html);
    }

    /// <summary>
    /// Avoids presenting absent legacy data as eight unearned badges.
    /// </summary>
    [Fact]
    public async Task MissingOwnershipRemainsUnknown()
    {
        string html = await RenderAsync(null);

        Assert.Equal(8, html.Split(_unknownClass).Length - 1);
        Assert.DoesNotContain(_earnedClass, html);
    }

    /// <summary>
    /// Renders the production badge strip with localized names.
    /// </summary>
    /// <param name="badges">The individual ownership flags.</param>
    /// <returns>The rendered strip.</returns>
    private static async Task<string> RenderAsync(IReadOnlyList<bool>? badges)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IStringLocalizer<TrackerResources>>(new BadgeLocalizer());
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        Dictionary<string, object?> parameters = new() { [nameof(GymBadgeProgress.Badges)] = badges };
        return await renderer.Dispatcher.InvokeAsync(async () => (await renderer.RenderComponentAsync<GymBadgeProgress>(ParameterView.FromDictionary(parameters))).ToHtmlString());
    }

    /// <summary>
    /// Resolves the production badge resource strings for component tests.
    /// </summary>
    private sealed class BadgeLocalizer : IStringLocalizer<TrackerResources>
    {
        private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets the production text for one resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets formatted production text for one resource key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Returns an empty enumeration because tests request individual resource keys.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent cultures are included.</param>
        /// <returns>An empty resource enumeration.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
