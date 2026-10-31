using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Core;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Threading.Channels;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Verifies real Archive and visible-target protocol requests distinguish user work from automatic or hidden work.
/// </summary>
public sealed partial class LookupPreparationPriorityTests
{
    private const string _runId = "lookup-priority-run";
    private const string _speciesId = "PIKACHU:0";
    private const string _version = "0.10.0";
    private const string _profile = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string _policy = "mixed";
    private const string _phase = "player_fusions";
    private const string _connection = "Connection";
    private const string _recipes = "_recipes";
    private const string _connectionStatus = "_connectionStatus";
    private const string _selection = "_selection";
    private const string _restart = "RestartObtainabilityPrecalculation";
    private const string _stop = "StopObtainabilityPrecalculation";
    private const string _selectRun = "SelectRun";
    private const string _selectSection = "SelectSection";
    private const string _showHistory = "ShowHistory";
    private const string _matches = "_matches";
    private const string _lookup = "_lookup";
    private const string _startSearchRefresh = "StartObtainabilityRefresh";
    private const string _startLookupRefresh = "StartLookupObtainabilityRefresh";
    private const string _parametersSet = "OnParametersSet";
    private const BindingFlags _members = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Verifies automatically opened Archive work stays in the background until a run or information section is chosen.
    /// </summary>
    /// <param name="selectRun">Whether explicit navigation chooses a run rather than its Pokemon tab.</param>
    /// <returns>Completion after inspecting actual serialized request priorities.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutomaticArchivePromotesAfterUserNavigation(bool selectRun)
    {
        using PriorityConnection connection = new();
        ArchiveView view = CreateArchive(connection.Requests, true);
        try
        {
            Invoke(view, _restart);
            TrackerMessage automatic = await connection.Stream.ReadRequestAsync();
            Assert.False(Payload(automatic).Foreground);

            Invoke(view, selectRun ? _selectRun : _selectSection, selectRun ? _runId : ArchiveSection.Pokemon);
            connection.Complete(automatic);
            TrackerMessage selected = await connection.Stream.ReadRequestAsync();
            Assert.True(Payload(selected).Foreground);
            Assert.Equal(_runId, Payload(selected).Recipe.RunId);

            Invoke(view, _showHistory);
            Assert.False(connection.Requests.ArchiveObtainabilityPrecalculationSelected);
        }
        finally
        {
            Invoke(view, _stop);
        }
    }

    /// <summary>
    /// Verifies entering Archive through the user's primary tab immediately prioritizes its unprepared selected run.
    /// </summary>
    /// <returns>Completion after inspecting the outgoing production request.</returns>
    [Fact]
    public async Task UserArchiveNavigationStartsForegroundPreparation()
    {
        using PriorityConnection connection = new();
        ArchiveView view = CreateArchive(connection.Requests, false);
        try
        {
            Invoke(view, _restart);
            Assert.True(Payload(await connection.Stream.ReadRequestAsync()).Foreground);
        }
        finally
        {
            Invoke(view, _stop);
        }
    }

    /// <summary>
    /// Verifies automatic completion navigation revokes a previous explicit Pokemon selection without requiring a tab reset.
    /// </summary>
    /// <returns>Completion after checking actual foreground and demoted request payloads.</returns>
    [Fact]
    public async Task AutomaticTransitionDemotesPreviouslyExplicitPokemonSelection()
    {
        using PriorityConnection connection = new();
        ArchiveView view = CreateArchive(connection.Requests, false);
        try
        {
            Invoke(view, _selectSection, ArchiveSection.Pokemon);
            Invoke(view, _restart);
            TrackerMessage explicitRequest = await connection.Stream.ReadRequestAsync();
            Assert.True(Payload(explicitRequest).Foreground);

            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ArchiveView.AutomaticSelection)] = true }).SetParameterProperties(view);
            Invoke(view, _parametersSet);
            connection.Complete(explicitRequest);
            TrackerMessage automaticRequest = await connection.Stream.ReadRequestAsync();
            Assert.False(Payload(automaticRequest).Foreground);
        }
        finally
        {
            Invoke(view, _stop);
        }
    }

    /// <summary>
    /// Verifies retained search, selected-Pokemon and fusion refresh loops stop renewing foreground priority when hidden.
    /// </summary>
    /// <param name="mode">Zero for Pokemon search, one for its selected card, or two for fusion results.</param>
    /// <returns>Completion after visible, hidden and visible-again production payloads have been checked.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task VisibleTargetRequestsFollowContainingPanelVisibility(int mode)
    {
        using PriorityConnection connection = new();
        PriorityActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton(connection.Requests);
        services.AddSingleton<IJSRuntime, Settings.SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, PriorityLocalizer>();
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _runId, Guid.NewGuid().ToString()));
        services.AddSingleton(new Ironmon.Tracker.Connection.Access.DiagnosticAccessService(storage, new Ironmon.Tracker.Access.DiagnosticAccessTokenValidator(new Ironmon.Tracker.Access.DiagnosticAccessKeyring([]))));
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            await renderer.RenderComponentAsync<PriorityHost>(ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(PriorityHost.Mode)] = mode }));
            object component = mode == 2 ? activator.Fusion! : activator.Explorer!;
            if (mode == 1)
            {
                SetField(component, _lookup, CreatePokemon());
            }
            else
            {
                SetField(component, _matches, new PokemonSearchMatch[] { new() { SpeciesId = _speciesId, SpeciesName = _speciesId, ObtainabilityStatus = PokemonObtainabilityStatus.Calculating } });
            }

            Invoke(component, mode == 1 ? _startLookupRefresh : _startSearchRefresh);
            TrackerMessage visible = await connection.Stream.ReadRequestAsync();
            Assert.True(Payload(visible).Foreground);
            Assert.True(mode == 1 ? Payload(visible).SpeciesId == _speciesId : Payload(visible).SpeciesIds.Contains(_speciesId));

            activator.Host!.SetVisible(false);
            connection.Complete(visible);
            TrackerMessage hidden = await connection.Stream.ReadRequestAsync();
            Assert.False(Payload(hidden).Foreground);

            activator.Host.SetVisible(true);
            connection.Complete(hidden);
            Assert.True(Payload(await connection.Stream.ReadRequestAsync()).Foreground);
        });
    }

    /// <summary>
    /// Creates real Archive selection logic without rendering unrelated data loaders.
    /// </summary>
    /// <param name="requests">The controlled production request client.</param>
    /// <param name="automatic">Whether reset navigation opened the archive automatically.</param>
    /// <returns>The initialized Archive coordinator.</returns>
    private static ArchiveView CreateArchive(TrackerRequestClient requests, bool automatic)
    {
        ArchiveView view = new();
        ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ArchiveView.AutomaticSelection)] = automatic }).SetParameterProperties(view);
        typeof(ArchiveView).GetProperty(_connection, _members)!.SetValue(view, requests);
        SetField(view, _recipes, new[] { CreateRecipe() });
        SetField(view, _connectionStatus, TrackerConnectionStatus.Connected);
        ArchiveRunSelectionState selection = (ArchiveRunSelectionState)typeof(ArchiveView).GetField(_selection, _members)!.GetValue(view)!;
        selection.Refresh([_runId]);
        selection.SetExpanded(true);
        return view;
    }

    /// <summary>
    /// Invokes a production interaction or refresh method.
    /// </summary>
    /// <param name="component">The production component.</param>
    /// <param name="method">The private method name.</param>
    /// <param name="arguments">The method arguments.</param>
    private static void Invoke(object component, string method, params object[] arguments)
        => component.GetType().GetMethod(method, _members)!.Invoke(component, arguments);

    /// <summary>
    /// Supplies controlled response data to the production component.
    /// </summary>
    /// <param name="component">The component whose request data is populated.</param>
    /// <param name="name">The private state field.</param>
    /// <param name="value">The test-owned response data.</param>
    private static void SetField(object component, string name, object value)
        => component.GetType().GetField(name, _members)!.SetValue(component, value);

    /// <summary>
    /// Decodes the production wire payload after checking its command.
    /// </summary>
    /// <param name="message">The captured outgoing request.</param>
    /// <returns>The actual request payload sent to the game.</returns>
    private static PokemonObtainabilityRequestPayload Payload(TrackerMessage message)
    {
        Assert.Equal(TrackerCommands.PokemonObtainability, message.Command);
        return TrackerJson.DeserializePayload<PokemonObtainabilityRequestPayload>(message.Payload);
    }

    /// <summary>
    /// Creates isolated valid reconstruction metadata without reading personal saves.
    /// </summary>
    /// <returns>The test-owned completed run.</returns>
    private static CompletedRunRecipePayload CreateRecipe() => new()
    {
        RunId = _runId, Result = _runId, GenerationProfileId = _profile, GameVersion = _version, IronmonVersion = _version,
        Configuration = new() { SchemaVersion = 1, WildPolicy = _policy, TrainerPolicy = _policy, UnfusionSetting = _policy },
        SpeciesGenerator = new() { Version = 1, PoolFingerprint = _profile }, AbilityGenerator = new() { Version = 1, PoolSize = 1, PoolFingerprint = _profile }, PlayerFusionGenerator = new() { Version = 1, PoolSize = 1, PoolFingerprint = _profile }
    };

    /// <summary>
    /// Creates one selected species whose exact status still needs a response.
    /// </summary>
    /// <returns>The isolated Pokemon card data.</returns>
    private static PokemonLookupSnapshot CreatePokemon()
        => new() { Identity = new() { SpeciesId = _speciesId, SpeciesName = _speciesId, Obtainability = new() { Status = PokemonObtainabilityStatus.Calculating } } };

    /// <summary>
    /// Owns one production protocol connection with no native game or user profile access.
    /// </summary>
    private sealed class PriorityConnection : IDisposable
    {
        private readonly TrackerRequestSession _session = new(new TrackerDiagnosticsStore());

        /// <summary>
        /// Connects the production client to the test-controlled output stream.
        /// </summary>
        internal PriorityConnection()
        {
            _session.Connect(new TrackerMessageWriter(Stream));
            TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _runId, Guid.NewGuid().ToString()));
            Requests = new(_session, new(0, _version, false, TimeSpan.FromSeconds(2)), new(), new AreaDiscoveryStore(storage));
        }

        /// <summary>
        /// Gets the captured production requests.
        /// </summary>
        internal PriorityStream Stream { get; } = new();

        /// <summary>
        /// Gets the production request client used by all components under test.
        /// </summary>
        internal TrackerRequestClient Requests { get; }

        /// <summary>
        /// Completes a poll while leaving its requested status unresolved for the next visibility check.
        /// </summary>
        /// <param name="message">The captured request to complete.</param>
        internal void Complete(TrackerMessage message)
            => Assert.True(_session.TryComplete(TrackerMessageFactory.CreateResponse(message.RequestId!, new PokemonObtainabilityResponsePayload { Phase = _phase, Target = new() { Status = PokemonObtainabilityStatus.Calculating } }, _runId)));

        /// <summary>
        /// Releases only test-owned request and stream resources.
        /// </summary>
        public void Dispose()
        {
            _session.Dispose();
            Stream.Dispose();
        }
    }

    /// <summary>
    /// Captures serialized messages while allowing tests to choose when a response arrives.
    /// </summary>
    private sealed class PriorityStream : MemoryStream
    {
        private readonly Channel<TrackerMessage> _requests = Channel.CreateUnbounded<TrackerMessage>();

        /// <summary>
        /// Captures each complete newline-delimited tracker request.
        /// </summary>
        /// <param name="buffer">The encoded protocol bytes.</param>
        /// <param name="cancellationToken">The write cancellation token.</param>
        /// <returns>Completion after the complete request has been queued.</returns>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await base.WriteAsync(buffer, cancellationToken);
            string line = System.Text.Encoding.UTF8.GetString(ToArray());
            if (!line.EndsWith('\n'))
                return;

            SetLength(0);
            Position = 0;
            await _requests.Writer.WriteAsync(TrackerMessageCodec.Deserialize(line.Trim()), cancellationToken);
        }

        /// <summary>
        /// Waits for the next actual request with a bounded test deadline.
        /// </summary>
        /// <returns>The captured request.</returns>
        internal async Task<TrackerMessage> ReadRequestAsync()
            => await _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Supplies the same visibility cascade used by retained Archive information panels.
    /// </summary>
    private sealed class PriorityHost : ComponentBase
    {
        private readonly CompletedRunRecipePayload _recipe = CreateRecipe();
        private readonly PokemonLookupSnapshot _pokemon = CreatePokemon();
        private bool _visible = true;

        /// <summary>
        /// Gets or sets which real target-refresh component is rendered.
        /// </summary>
        [Parameter]
        public int Mode { get; set; }

        /// <summary>
        /// Changes panel visibility without unmounting the retained child.
        /// </summary>
        /// <param name="visible">Whether the panel is visible to the user.</param>
        internal void SetVisible(bool visible)
        {
            _visible = visible;
            StateHasChanged();
        }

        /// <summary>
        /// Renders production refresh components beneath the Archive visibility cascade.
        /// </summary>
        /// <param name="builder">The Blazor render tree builder.</param>
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<bool>>(0);
            builder.AddAttribute(1, nameof(CascadingValue<bool>.Name), nameof(PokemonLookupExplorer.ForegroundPreparation));
            builder.AddAttribute(2, nameof(CascadingValue<bool>.Value), _visible);
            builder.AddAttribute(3, nameof(CascadingValue<bool>.ChildContent), (RenderFragment)(child =>
            {
                child.OpenComponent(0, Mode == 2 ? typeof(IronmonFusionSearch) : typeof(PokemonLookupExplorer));
                child.AddAttribute(1, nameof(PokemonLookupExplorer.Recipe), _recipe);
                if (Mode == 2)
                    child.AddAttribute(2, nameof(IronmonFusionSearch.Pokemon), _pokemon);

                child.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }

    /// <summary>
    /// Captures rendered production components without replacing their behavior.
    /// </summary>
    private sealed class PriorityActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the host whose visibility can be changed.
        /// </summary>
        internal PriorityHost? Host { get; private set; }

        /// <summary>
        /// Gets the real Pokemon explorer under test.
        /// </summary>
        internal PokemonLookupExplorer? Explorer { get; private set; }

        /// <summary>
        /// Gets the real fusion search under test.
        /// </summary>
        internal IronmonFusionSearch? Fusion { get; private set; }

        /// <summary>
        /// Creates production components and retains the three relevant instances.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The newly created component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is PriorityHost host)
                Host = host;

            if (component is PokemonLookupExplorer explorer)
                Explorer = explorer;

            if (component is IronmonFusionSearch fusion)
                Fusion = fusion;

            return component;
        }
    }

    /// <summary>
    /// Supplies stable local labels without starting desktop application services.
    /// </summary>
    private sealed class PriorityLocalizer : IStringLocalizer<TrackerResources>
    {
        /// <summary>
        /// Gets the label for one localization key.
        /// </summary>
        public LocalizedString this[string name] => new(name, name);

        /// <summary>
        /// Gets the label for one formatted localization key.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        /// <summary>
        /// Supplies the unused resource enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent culture resources are included.</param>
        /// <returns>An empty resource sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
            => [];
    }
}
