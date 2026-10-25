using Ironmon.Tracker.App.Components.Wardrobe;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Ironmon.Tracker.Protocol.Wardrobe;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Ironmon.Tracker.App.Tests.Wardrobe;

/// <summary>
/// Renders the production wardrobe through game-issued lifecycle and compact layout states.
/// </summary>
public sealed class WardrobeViewTests
{
    private const string _game = "wardrobe-view-game";
    private const string _wardrobe = "11111111111111111111111111111111";
    private const string _profile = "22222222222222222222222222222222";
    private const string _version = "0.9.0";
    private const string _root = "IronmonWardrobeViews";
    private const string _render = "StateHasChanged";
    private const string _category = "_category";
    private const string _maximized = "_maximized";
    private const string _title = "wardrobe-title";
    private const string _owned = "clothes:owned";
    private const string _locked = "clothes:locked";
    private const string _ownedId = "owned";
    private const string _lockedId = "locked";
    private const string _hairId = "style";
    private const string _hair = "1_style";
    private const string _hairKey = "hair:style";
    private const string _slot = "slot:File A";
    private const string _accessory = "roundglasses";
    private const string _accessory2 = "flower";
    private const string _accessoryKey = "hat:roundglasses";
    private const string _accessory2Key = "hat:flower";
    private const string _colors = "colors";
    private const string _profiles = "profiles";
    private const string _previewPrefix = "wardrobe-";
    private const string _extraPrefix = "extra-";

    /// <summary>
    /// Verifies the host exposes no opener and the real dialog retains concise labels across its editing states.
    /// </summary>
    /// <returns>Completion after production component lifecycle and markup checks.</returns>
    [Fact]
    public async Task GameEventOpensCompactObsidianDialogAndCloseEventRemovesIt()
    {
        string root = Path.Combine(Path.GetTempPath(), _root, Guid.NewGuid().ToString());
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        TrackerRequestClient requests = new(session, new TrackerConnectionOptions(0, _version, false, TimeSpan.FromSeconds(2)), new(), new AreaDiscoveryStore(new(root)));
        CapturingActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddLocalization(options => options.ResourcesPath = TrackerLocalizationConstants.ResourcesPath);
        services.AddSingleton<Microsoft.JSInterop.IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton(requests);
        services.AddSingleton<TrackerWindowService>();
        services.AddSingleton<IComponentActivator>(activator);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        try
        {
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var host = await renderer.RenderComponentAsync<WardrobeDialogHost>(ParameterView.Empty);
                Assert.DoesNotContain(_title, host.ToHtmlString());
                Assert.DoesNotContain("button", host.ToHtmlString());
                GameHandshakePayload game = new(_version, _version, true, false, root, null, null) { SessionId = _game, WardrobeProtocolVersion = 1 };
                requests.Wardrobe.Connect(game);
                WardrobeSnapshotPayload snapshot = Snapshot();
                requests.Wardrobe.Observe(snapshot, game);
                string owned = host.ToHtmlString();
                Assert.Contains("wardrobe-dialog", owned);
                Assert.Contains("Apply &amp; return", owned);
                Assert.Contains("Search outfits", owned);
                Assert.DoesNotContain("Preview changes", owned);
                Assert.DoesNotContain("Game is waiting", owned);
                Assert.Contains("aria-hidden=\"true\"", owned);
                Export(_ownedId, owned);

                snapshot = snapshot with { Revision = 1, Appearance = snapshot.Appearance with { Clothes = _lockedId }, Catalog = null };
                requests.Wardrobe.Observe(snapshot, game);
                string locked = host.ToHtmlString();
                Assert.Contains("Unlock · 80", System.Net.WebUtility.HtmlDecode(locked));
                Assert.Single(System.Text.RegularExpressions.Regex.Matches(locked, "ow-preview-action"));
                Export(_lockedId, locked);
                snapshot = snapshot with { Revision = 2, Points = 10 };
                requests.Wardrobe.Observe(snapshot, game);
                Export("insufficient", host.ToHtmlString());

                foreach (string category in new[] { WardrobeProtocol.Hair, WardrobeProtocol.Hat, _colors, _profiles })
                {
                    Set(activator.Dialog!, _category, category);
                    Render(activator.Dialog!);
                    string markup = host.ToHtmlString();
                    Export(category, markup);
                    if (category == WardrobeProtocol.Hair)
                        Assert.Contains("Base hair color", markup);
                    if (category == _profiles)
                    {
                        Assert.Contains("Evening", markup);
                        Assert.Contains("Rename", markup);
                        Assert.Contains("Replace", markup);
                        Assert.Contains("Delete", markup);
                    }
                }

                Set(activator.Dialog!, _maximized, true);
                Render(activator.Dialog!);
                Export("expanded", host.ToHtmlString());
                Set(activator.Dialog!, _maximized, false);
                requests.Wardrobe.Disconnect();
                Assert.Contains("Disconnected", host.ToHtmlString());
                Export("disconnected", host.ToHtmlString());
                requests.Wardrobe.Connect(game);
                requests.Wardrobe.Observe(snapshot with { Revision = 3, Open = false }, game);
                Assert.DoesNotContain(_title, host.ToHtmlString());
            });
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    /// <summary>
    /// Builds a game-authoritative view fixture with long names and independent accessory slots.
    /// </summary>
    /// <returns>The complete opening payload.</returns>
    private static WardrobeSnapshotPayload Snapshot()
    {
        WardrobeAppearancePayload appearance = new() { Clothes = _ownedId, Hair = _hair, Hat = _accessory, Hat2 = _accessory2, SkinTone = 3, HairColor = 137 };
        return new()
        {
            ProtocolVersion = 1, GameSessionId = _game, WardrobeId = _wardrobe, AppearanceKey = _slot, Open = true, Points = 500,
            Appearance = appearance, EquippedAppearance = appearance, OwnedKeys = [_owned, _hairKey, _accessoryKey, _accessory2Key],
            OutfitProfiles = [new() { Id = _profile, Name = "Evening", Appearance = appearance, Available = true }],
            Catalog = [new() { Key = _owned, Id = _ownedId, Category = WardrobeProtocol.Clothes, Name = "Pokemon League Champion's Ceremonial Outfit", Points = 80, Available = true },
                new() { Key = _locked, Id = _lockedId, Category = WardrobeProtocol.Clothes, Name = "Adventurer", Points = 80, Available = true },
                new() { Key = _hairKey, Id = _hairId, Category = WardrobeProtocol.Hair, Name = "Shoulder-length waves", Variants = [1, 2, 3, 4, 5, 6, 7, 8], Available = true },
                new() { Key = _accessoryKey, Id = _accessory, Category = WardrobeProtocol.Hat, Name = "Round glasses", Available = true },
                new() { Key = _accessory2Key, Id = _accessory2, Category = WardrobeProtocol.Hat, Name = "Flower", Available = true },
                .. Enumerable.Range(1, 6).Select(index => new WardrobeCatalogItemPayload { Key = WardrobeProtocol.Clothes + ':' + _extraPrefix + index, Id = _extraPrefix + index, Category = WardrobeProtocol.Clothes, Name = "Travelling Pokemon Trainer's Ceremonial Outfit " + index, Points = 80, Available = true }),
                .. Enumerable.Range(1, 2).Select(index => new WardrobeCatalogItemPayload { Key = WardrobeProtocol.Hair + ':' + _extraPrefix + index, Id = _extraPrefix + index, Category = WardrobeProtocol.Hair, Name = "Windswept hairstyle " + index, Variants = [1, 2, 3, 4, 5, 6, 7, 8], Available = true }),
                .. Enumerable.Range(1, 4).Select(index => new WardrobeCatalogItemPayload { Key = WardrobeProtocol.Hat + ':' + _extraPrefix + index, Id = _extraPrefix + index, Category = WardrobeProtocol.Hat, Name = "Travelling Trainer's accessory " + index, Available = true })]
        };
    }

    /// <summary>
    /// Updates an inspectable local presentation state on the rendered dialog.
    /// </summary>
    /// <param name="dialog">The mounted production component.</param>
    /// <param name="field">The private presentation field.</param>
    /// <param name="value">The requested state value.</param>
    private static void Set(WardrobeDialog dialog, string field, object value)
        => typeof(WardrobeDialog).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, value);

    /// <summary>
    /// Requests rendering after a local presentation state change.
    /// </summary>
    /// <param name="dialog">The mounted production component.</param>
    private static void Render(WardrobeDialog dialog)
        => typeof(ComponentBase).GetMethod(_render, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, null);

    /// <summary>
    /// Exports actual production markup and styles for viewport inspection.
    /// </summary>
    /// <param name="name">The view-state identifier.</param>
    /// <param name="html">The renderer-produced component markup.</param>
    private static void Export(string name, string html)
        => RunManagementViewTests.ExportPreview(_previewPrefix + name, html);

    /// <summary>
    /// Retains the production dialog while creating ordinary components normally.
    /// </summary>
    private sealed class CapturingActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the currently mounted wardrobe dialog.
        /// </summary>
        public WardrobeDialog? Dialog { get; private set; }

        /// <summary>
        /// Creates a component and retains the wardrobe for presentation-state checks.
        /// </summary>
        /// <param name="componentType">The component type requested by the renderer.</param>
        /// <returns>The new component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is WardrobeDialog dialog)
                Dialog = dialog;

            return component;
        }
    }
}
