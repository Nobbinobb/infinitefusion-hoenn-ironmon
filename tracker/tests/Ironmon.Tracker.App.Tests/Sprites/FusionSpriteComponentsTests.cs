using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Components.Player;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Protocol.Live;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ironmon.Tracker.App.Tests.Sprites;

/// <summary>
/// Verifies exact visual-component ownership, legacy fallback, and one shared sprite viewer.
/// </summary>
public sealed class FusionSpriteComponentsTests
{
    private const string _fixtureFolder = "ironmon-fusion-sprite-tests";
    private const string _spritePath = "Graphics/CustomBattlers/local_sprites/IronmonTracker/custom-94-6-a.png";
    private const string _headPath = "Graphics/head.png";
    private const string _bodyPath = "Graphics/body.png";
    private const string _metadataFolder = "Graphics/CustomBattlers/local_sprites/IronmonTracker";
    private const string _metadataPrefix = "components-";
    private const string _metadataExtension = ".json";
    private const string _headName = "Gengar";
    private const string _bodyName = "Charizard";
    private const string _fusionName = "Genzard";
    private const string _nickname = "Thunderstorm";
    private const string _recipeIdentity = "B1H2:0";
    private const string _pokemonId = "visual-sprite-player";
    private const string _gender = "male";
    private const string _status = "NONE";
    private const string _ability = "Cursed Body";
    private const string _dialogMarkup = "role=\"dialog\"";
    private const string _componentsMarkup = "obsidian-fusion-components";
    private const string _selectMethod = "SelectImage";
    private const string _renderMethod = "StateHasChanged";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _culture = "en-US";
    private const string _outsidePath = "../outside.png";
    private const string _spriteOpenMethod = "OpenDialog";
    private const string _speciesMarkup = "<p class=\"obsidian-species\">";
    private const string _badgeMarkup = "obsidian-badge-art";
    private const string _headAltMarkup = "alt=\"Gengar\"";
    private const string _bodyAltMarkup = "alt=\"Charizard\"";

    /// <summary>
    /// Loads displayed head/body metadata independently of a gameplay fusion identity.
    /// </summary>
    /// <returns>A task that completes after the renamed player's rendered identity and badge assertions.</returns>
    [Fact]
    public async Task RenamedPlayerShowsVisualComponentsAndPreservesItsNickname()
    {
        using SpriteFixture fixture = new();
        await RenderAsync<PlayerCard>(fixture, new() { [nameof(PlayerCard.Player)] = Player(), [nameof(PlayerCard.GameRoot)] = fixture.Root }, async (_, html, _) =>
        {
            string rendered = WebUtility.HtmlDecode(html());
            Assert.Contains(_nickname, rendered);
            Assert.Contains(_headName + " / " + _bodyName, rendered);
            Assert.Contains(_componentsMarkup, rendered);
            Assert.DoesNotContain(_speciesMarkup + _fusionName, rendered);
            Assert.Contains(_fusionName, rendered);
            Assert.Equal(8, rendered.Split(_badgeMarkup).Length - 1);
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// Keeps the original species/level line for unavailable legacy metadata.
    /// </summary>
    /// <returns>A task that completes after the fallback identity assertions.</returns>
    [Fact]
    public async Task LegacySpriteRetainsOriginalSpeciesLine()
    {
        using SpriteFixture fixture = new();
        File.Delete(fixture.MetadataPath);
        await RenderAsync<PlayerCard>(fixture, new() { [nameof(PlayerCard.Player)] = Player(), [nameof(PlayerCard.GameRoot)] = fixture.Root }, async (_, html, _) =>
        {
            string rendered = WebUtility.HtmlDecode(html());
            Assert.DoesNotContain(_componentsMarkup, rendered);
            Assert.Contains(_speciesMarkup + _fusionName, rendered);
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// Switches enlarged components in one dialog while retaining the original request.
    /// </summary>
    /// <returns>A task that completes after opening, switching, and closing the shared viewer.</returns>
    [Fact]
    public async Task SharedViewerSwitchesImagesWithoutOpeningAnotherDialog()
    {
        using SpriteFixture fixture = new();
        await RenderAsync<PokemonSpriteDialogHost>(fixture, [], async (host, html, service) =>
        {
            service.Open(LocalSpriteLoader.Load(fixture.Root, _spritePath)!, _fusionName, 288, fixture.Root, _spritePath, PokemonSpritePart.Head);
            PokemonSpriteDialogRequest request = service.Current!;
            await host.InvokeRenderAsync();
            Assert.Equal(3, request.Images.Count);
            Assert.Equal(1, html().Split(_dialogMarkup).Length - 1);
            Assert.Contains(_headAltMarkup, html());
            typeof(PokemonSpriteDialogHost).GetMethod(_selectMethod, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(host.Component, [PokemonSpritePart.Body]);
            await host.InvokeRenderAsync();
            Assert.Same(request, service.Current);
            Assert.Equal(1, html().Split(_dialogMarkup).Length - 1);
            Assert.Contains(_bodyAltMarkup, html());
            service.Close();
            await host.InvokeRenderAsync();
            Assert.DoesNotContain(_dialogMarkup, html());
        });
    }

    /// <summary>
    /// Retains the original one-image viewer without component metadata.
    /// </summary>
    [Fact]
    public void MissingMetadataDoesNotInventComponents()
    {
        using SpriteFixture fixture = new();
        File.Delete(fixture.MetadataPath);
        PokemonSpriteDialogService service = new();
        service.Open(LocalSpriteLoader.Load(fixture.Root, _spritePath)!, _fusionName, 288, fixture.Root, _spritePath);
        Assert.Single(service.Current!.Images);
    }

    /// <summary>
    /// Rejects a sidecar belonging to a different recorded sprite variant.
    /// </summary>
    [Fact]
    public void DifferentSpritePathDoesNotSubstituteRecordedComponents()
    {
        using SpriteFixture fixture = new();
        fixture.WriteMetadata(_headName, _bodyName, _bodyPath);
        string json = File.ReadAllText(fixture.MetadataPath).Replace(_spritePath, _headPath, StringComparison.Ordinal);
        File.WriteAllText(fixture.MetadataPath, json);
        Assert.Null(LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath));
    }

    /// <summary>
    /// Rejects component paths outside the owning game installation.
    /// </summary>
    [Fact]
    public void ComponentPathCannotEscapeTheInstallation()
    {
        using SpriteFixture fixture = new();
        fixture.WriteMetadata(_headName, _bodyName, _outsidePath);
        Assert.Null(LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath));
    }

    /// <summary>
    /// Refreshes cached names after a new game-language export.
    /// </summary>
    [Fact]
    public void ChangedMetadataInvalidatesTheParsedCache()
    {
        using SpriteFixture fixture = new();
        Assert.Equal(_headName, LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath)!.Head.Name);
        fixture.WriteMetadata(_bodyName, _headName, _bodyPath);
        File.SetLastWriteTimeUtc(fixture.MetadataPath, DateTime.UtcNow.AddSeconds(2));
        Assert.Equal(_bodyName, LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath)!.Head.Name);
    }

    /// <summary>
    /// Retries metadata that was unavailable during the first render.
    /// </summary>
    [Fact]
    public void MissingMetadataCanAppearAfterTheSpriteCacheIsRepaired()
    {
        using SpriteFixture fixture = new();
        File.Delete(fixture.MetadataPath);
        Assert.Null(LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath));
        fixture.WriteMetadata(_headName, _bodyName, _bodyPath);
        Assert.NotNull(LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath));
    }

    /// <summary>
    /// Falls back when images are removed after a successful component lookup.
    /// </summary>
    [Fact]
    public void RemovedComponentImagesInvalidateCachedPresentation()
    {
        using SpriteFixture fixture = new();
        Assert.NotNull(LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath));
        File.Delete(Path.Combine(fixture.Root, _headPath));
        Assert.Null(LocalSpriteComponentsLoader.Load(fixture.Root, _spritePath));
        PokemonSpriteDialogService service = new();
        service.Open(LocalSpriteLoader.Load(fixture.Root, _spritePath)!, _fusionName, 288, fixture.Root, _spritePath);
        Assert.Single(service.Current!.Images);
    }

    /// <summary>
    /// Uses the upgraded shared viewer from every ordinary sprite control.
    /// </summary>
    /// <returns>A task that completes after the shared sprite control's dialog request is verified.</returns>
    [Fact]
    public async Task SharedSpriteControlOpensFusionAndBothComponents()
    {
        using SpriteFixture fixture = new();
        await RenderAsync<PokemonSprite>(fixture, new() { [nameof(PokemonSprite.GameRoot)] = fixture.Root, [nameof(PokemonSprite.SpritePath)] = _spritePath, [nameof(PokemonSprite.Label)] = _fusionName }, async (sprite, _, service) =>
        {
            typeof(PokemonSprite).GetMethod(_spriteOpenMethod, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(sprite.Component, null);
            Assert.Equal(3, service.Current!.Images.Count);
            Assert.Equal(PokemonSpritePart.Fusion, service.Current.SelectedPart);
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// Creates a player whose gameplay species identity intentionally differs from its visual sprite.
    /// </summary>
    /// <returns>A renamed player with visual sprite metadata independent of its gameplay species identity.</returns>
    private static PlayerPokemonSnapshot Player()
        => new() { PokemonId = _pokemonId, SpeciesId = _recipeIdentity, Nickname = _nickname, SpeciesName = _fusionName, Fusion = true, SpritePath = _spritePath, Gender = _gender, Status = _status, Ability = _ability, Level = 32, Healing = new() };

    /// <summary>
    /// Renders real production components with detached test-owned storage.
    /// </summary>
    /// <typeparam name="T">The production root component to render and capture.</typeparam>
    /// <param name="fixture">The fixture supplying the isolated game directory and sprite metadata.</param>
    /// <param name="parameters">The parameters passed to the root component.</param>
    /// <param name="check">The callback receiving the captured component, current HTML accessor, and shared dialog service on the renderer's dispatcher.</param>
    /// <returns>A task that completes after rendering, running the assertions, and disposing the test services.</returns>
    private static async Task RenderAsync<T>(SpriteFixture fixture, Dictionary<string, object?> parameters, Func<CapturingActivator<T>, Func<string>, PokemonSpriteDialogService, Task> check) where T : IComponent
    {
        ServiceCollection services = new();
        PokemonSpriteDialogService dialog = new();
        CapturingActivator<T> activator = new();
        services.AddLogging();
        services.AddSingleton(dialog);
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, DetachedJs>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, ProductionLocalizer>();
        services.AddSingleton(new TrackerKnowledgeStore(new TrackerKnowledgeOptions(fixture.Root)));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var component = await renderer.RenderComponentAsync<T>(ParameterView.FromDictionary(parameters));
            await check(activator, component.ToHtmlString, dialog);
        });
    }

    /// <summary>
    /// Owns exact-path sprite metadata and distinct image source bytes.
    /// </summary>
    private sealed class SpriteFixture : IDisposable
    {
        /// <summary>
        /// Gets the isolated installation directory.
        /// </summary>
        public string Root { get; } = Path.Combine(Path.GetTempPath(), _fixtureFolder, Guid.NewGuid().ToString());

        /// <summary>
        /// Gets the cross-language metadata path.
        /// </summary>
        public string MetadataPath { get; }

        /// <summary>
        /// Creates distinct image-source files and the game's component metadata schema in an isolated directory.
        /// </summary>
        public SpriteFixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, _metadataFolder));
            File.WriteAllBytes(Path.Combine(Root, _spritePath), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(Root, _headPath), [4, 5, 6]);
            File.WriteAllBytes(Path.Combine(Root, _bodyPath), [7, 8, 9]);
            string digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(_spritePath.ToLowerInvariant())));
            MetadataPath = Path.Combine(Root, _metadataFolder, _metadataPrefix + digest + _metadataExtension);
            WriteMetadata(_headName, _bodyName, _bodyPath);
        }

        /// <summary>
        /// Writes the JSON schema emitted by the bundled game.
        /// </summary>
        /// <param name="head">The display name stored for the visual head.</param>
        /// <param name="body">The display name stored for the visual body.</param>
        /// <param name="bodyPath">The body image path stored in the metadata.</param>
        public void WriteMetadata(string head, string body, string bodyPath)
            => File.WriteAllText(MetadataPath, JsonSerializer.Serialize(new { schema_version = 1, sprite_path = _spritePath, head = new { name = head, sprite_path = _headPath }, body = new { name = body, sprite_path = bodyPath } }));

        /// <summary>
        /// Removes only the fixture's uniquely owned temporary directory.
        /// </summary>
        public void Dispose()
            => Directory.Delete(Root, true);
    }

    /// <summary>
    /// Captures a production component for direct UI event verification.
    /// </summary>
    /// <typeparam name="T">The component type whose most recently created instance is retained.</typeparam>
    private sealed class CapturingActivator<T> : IComponentActivator where T : IComponent
    {
        /// <summary>
        /// Gets the most recently created instance of the target component type.
        /// </summary>
        public T Component { get; private set; } = default!;

        /// <summary>
        /// Constructs the requested component.
        /// </summary>
        /// <param name="componentType">The component type requested by the renderer.</param>
        /// <returns>The newly constructed component, also retained when it matches <typeparamref name="T"/>.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is T target)
                Component = target;
            return component;
        }

        /// <summary>
        /// Requests a render of the captured component after an interaction.
        /// </summary>
        /// <returns>A completed task after requesting the render on the caller's renderer dispatcher.</returns>
        public Task InvokeRenderAsync()
        {
            typeof(ComponentBase).GetMethod(_renderMethod, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(Component, null);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Supplies production localization strings to detached views.
    /// </summary>
    private sealed class ProductionLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets an English production localization resource.
        /// </summary>
        /// <param name="name">The localization resource key.</param>
        /// <returns>The localized value, or the resource key when no value is available.</returns>
        public LocalizedString this[string name] => new(name, _resources.GetString(name, CultureInfo.GetCultureInfo(_culture)) ?? name);

        /// <summary>
        /// Gets a formatted English production localization resource.
        /// </summary>
        /// <param name="name">The localization resource key.</param>
        /// <param name="arguments">The values substituted into the resource text.</param>
        /// <returns>The formatted localization resource.</returns>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Provides the unused localization enumeration contract for these tests.
        /// </summary>
        /// <param name="includeParentCultures">The unused request to include resources from parent cultures.</param>
        /// <returns>An empty sequence because the tests resolve individual resource keys.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }

    /// <summary>
    /// Supplies inert browser calls during static rendering.
    /// </summary>
    private sealed class DetachedJs : IJSRuntime
    {
        /// <summary>
        /// Returns a default result without invoking browser JavaScript.
        /// </summary>
        /// <typeparam name="TValue">The expected JavaScript result type.</typeparam>
        /// <param name="identifier">The unused JavaScript function identifier.</param>
        /// <param name="args">The unused JavaScript arguments.</param>
        /// <returns>A completed value task containing the default value of <typeparamref name="TValue"/>.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        /// <summary>
        /// Returns a default result without invoking browser JavaScript or observing cancellation.
        /// </summary>
        /// <typeparam name="TValue">The expected JavaScript result type.</typeparam>
        /// <param name="identifier">The unused JavaScript function identifier.</param>
        /// <param name="cancellationToken">The unused cancellation token.</param>
        /// <param name="args">The unused JavaScript arguments.</param>
        /// <returns>A completed value task containing the default value of <typeparamref name="TValue"/>.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);
    }
}
