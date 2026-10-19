using Ironmon.Tracker.App.Components.Pages;
using Ironmon.Tracker.Collection;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Checks earned-only rendering and stylesheet delivery in the built desktop application.
/// </summary>
public sealed class AchievementsPageTests
{
    private const string _prefix = "Ironmon-Achievement-UI-";
    private const string _empty = "Your collection starts here";
    private const string _webRoot = "wwwroot";
    private const string _hostPage = "index.html";
    private const string _stylesheet = "obsidian-achievements.css";
    private const string _stylesheetLink = "<link rel=\"stylesheet\" href=\"obsidian-achievements.css\" />";
    private const string _listSelector = ".achievement-scroll";
    private const string _rowSelector = ".achievement-row";
    private const string _rowMarkup = "<li class=\"achievement-row\"";
    private const string _scopedSelector = "::deep";
    private const string _progressMaximum = "aria-valuemax=\"{0}\"";
    private const string _imagePrefix = "data:image/png;base64,";
    private const string _previewAction = "Preview notification";
    private const string _batch = """
        {"schema_version":1,"profile_id":"11111111111111111111111111111111","epoch_id":"22222222222222222222222222222222","stream_id":"33333333333333333333333333333333","events":[{"sequence":"1","type":"battle.started","attempt_id":"55555555555555555555555555555555","battle_id":"rival","context":{},"payload":{"battle_kind":"trainer","encounter":{"trainer_class_ids":["RIVAL1"],"authored_entry_ids":[]}}},{"sequence":"2","type":"battle.ended","attempt_id":"55555555555555555555555555555555","battle_id":"rival","context":{},"payload":{"outcome":"win"}}]}
        """;

    /// <summary>
    /// Shows earned names and conditions while withholding locked records.
    /// </summary>
    /// <param name="earn">Whether a rival victory should be committed before rendering.</param>
    /// <returns>A task completing after production markup and page disposal are checked.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PageShowsOnlyEarnedNames(bool earn)
    {
        string directory = Path.Combine(Path.GetTempPath(), _prefix + Guid.NewGuid());
        try
        {
            await using GameplayCollectionStore store = new(directory);
            if (earn)
                await store.AcceptBatchAsync(JsonSerializer.Deserialize<JsonElement>(_batch));

            ServiceCollection services = new();
            services.AddLogging();
            services.AddLocalization(options => options.ResourcesPath = TrackerLocalizationConstants.ResourcesPath);
            services.AddSingleton(store);
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            string markup = await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var page = await renderer.RenderComponentAsync<AchievementsPage>();
                return page.ToHtmlString();
            });
            var earned = await store.GetAchievementsAsync();
            Assert.Contains(_previewAction, markup);
            Assert.Equal(earned.Count, markup.Split(_rowMarkup, StringSplitOptions.None).Length - 1);
            if (earn)
            {
                Assert.NotEmpty(earned);
                Assert.Contains(_imagePrefix, markup);
                Assert.Contains(string.Format(System.Globalization.CultureInfo.InvariantCulture, _progressMaximum, GameplayCollectionStore.AchievementCount), markup);
                foreach (var achievement in earned)
                {
                    Assert.Contains(System.Net.WebUtility.HtmlEncode(achievement.Name), markup);
                    Assert.Contains(System.Net.WebUtility.HtmlEncode(achievement.Condition), markup);
                }
            }
            else
            {
                Assert.Contains(_empty, markup);
                Assert.DoesNotContain(_imagePrefix, markup);
            }
        }
        finally
        {
            if (Directory.Exists(directory) && Path.GetFileName(directory).StartsWith(_prefix, StringComparison.Ordinal))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Verifies that the host page links a stylesheet actually delivered in the desktop web root.
    /// </summary>
    [Fact]
    public void AchievementStylesShipWithTheHostPage()
    {
        string root = Path.Combine(AppContext.BaseDirectory, _webRoot);
        string host = File.ReadAllText(Path.Combine(root, _hostPage));
        string css = File.ReadAllText(Path.Combine(root, _stylesheet));
        Assert.Contains(_stylesheetLink, host);
        Assert.Contains(_listSelector, css);
        Assert.Contains(_rowSelector, css);
        Assert.DoesNotContain(_scopedSelector, css);
    }
}
