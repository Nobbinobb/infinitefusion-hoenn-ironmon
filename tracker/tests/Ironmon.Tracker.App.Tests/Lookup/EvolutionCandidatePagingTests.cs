using Ironmon.Tracker.App.Components.Lookup;
using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.App.Tests.Settings;
using Ironmon.Tracker.Connection.Areas;
using Ironmon.Tracker.Connection.Diagnostics;
using Ironmon.Tracker.Connection.Knowledge;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Lookup;
using Ironmon.Tracker.Protocol.Transport;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;
using System.Threading.Channels;

namespace Ironmon.Tracker.App.Tests.Lookup;

/// <summary>
/// Exercises candidate paging against a controlled protocol connection with delayed and failed responses.
/// </summary>
public sealed class EvolutionCandidatePagingTests
{
    private const string _runId = "candidate-paging-test";
    private const string _speciesId = "B1H4:0";
    private const string _otherSpeciesId = "B4H1:0";
    private const string _firstName = "First candidate";
    private const string _nextName = "Next candidate";
    private const string _newSourceName = "New source candidate";
    private const string _nextPage = "NextPageAsync";
    private const string _retryPage = "RetryPageAsync";
    private const string _render = "StateHasChanged";
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const BindingFlags _privateMembers = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Retains rows and range during a delayed page, a connection failure, and a retry, then replaces them together.
    /// </summary>
    /// <returns>A task representing the complete paging interaction.</returns>
    [Fact]
    public async Task FailedPageRetainsRowsAndRetryRequestsTheSameOffset()
    {
        await WithViewAsync(async (component, html, session, stream) =>
        {
            Task pending = Interact(component, _nextPage);
            Assert.False(pending.IsCompleted);
            TrackerMessage request = await stream.ReadRequestAsync();
            Assert.Equal(12, TrackerJson.DeserializePayload<EvolutionCandidateSearchRequestPayload>(request.Payload).Offset);
            Assert.Equal(12, TrackerJson.DeserializePayload<EvolutionCandidateSearchRequestPayload>(request.Payload).Limit);
            Assert.Contains(_firstName, html());
            Assert.Contains("aria-busy=\"true\"", html());
            Assert.Contains("class=\"evolution-target-action\" disabled", html());
            Assert.Contains("1\u201312", html());

            session.Disconnect();
            await pending;
            Render(component);
            Assert.Contains(_firstName, html());
            Assert.Contains("role=\"alert\"", html());
            Assert.Contains("1\u201312", html());
            session.Connect(new TrackerMessageWriter(stream));

            Task retry = Interact(component, _retryPage);
            TrackerMessage repeated = await stream.ReadRequestAsync();
            Assert.Equal(12, TrackerJson.DeserializePayload<EvolutionCandidateSearchRequestPayload>(repeated.Payload).Offset);
            Assert.Contains(_firstName, html());
            Complete(session, repeated, _nextName);
            await retry;
            Render(component);
            Assert.Contains(_nextName, html());
            Assert.DoesNotContain(_firstName, html());
            Assert.DoesNotContain("class=\"evolution-target-action\" disabled", html());
            Assert.Contains("13\u201324", html());
            Assert.Contains("aria-busy=\"false\"", html());
        });
    }

    /// <summary>
    /// Cancels an old page when another species is selected and ignores the old response if it arrives later.
    /// </summary>
    /// <returns>A task representing overlapping source requests.</returns>
    [Fact]
    public async Task SourceChangeReplacesPendingPageWithoutAcceptingItsLateResponse()
    {
        await WithViewAsync(async (component, html, session, stream) =>
        {
            Task oldPage = Interact(component, _nextPage);
            Assert.False(oldPage.IsCompleted);
            TrackerMessage oldRequest = await stream.ReadRequestAsync();
            Task newSource = component.SetParametersAsync(Parameters(_otherSpeciesId));
            TrackerMessage newRequest = await stream.ReadRequestAsync();
            Assert.Equal(_otherSpeciesId, TrackerJson.DeserializePayload<EvolutionCandidateSearchRequestPayload>(newRequest.Payload).SpeciesId);
            Complete(session, newRequest, _newSourceName);
            await newSource;
            await oldPage;
            Assert.False(Complete(session, oldRequest, _nextName));
            Render(component);
            Assert.Contains(_newSourceName, html());
            Assert.DoesNotContain(_nextName, html());
            Assert.Contains("aria-busy=\"false\"", html());
        });
    }

    /// <summary>
    /// Creates a real candidate list with isolated storage and a protocol stream controlled by the test.
    /// </summary>
    /// <param name="check">The paging interactions performed on the renderer dispatcher.</param>
    /// <returns>A task representing setup, rendering, and assertions.</returns>
    private static async Task WithViewAsync(Func<EvolutionCandidateList, Func<string>, TrackerRequestSession, CandidateStream, Task> check)
    {
        using TrackerRequestSession session = new(new TrackerDiagnosticsStore());
        using CandidateStream stream = new();
        session.Connect(new TrackerMessageWriter(stream));
        TrackerKnowledgeOptions storage = new(Path.Combine(Path.GetTempPath(), _runId, Guid.NewGuid().ToString()));
        TrackerRequestClient requests = new(session, new(0, _runId, false, TimeSpan.FromSeconds(2)), new(), new AreaDiscoveryStore(storage));
        CandidateActivator activator = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(requests);
        services.AddSingleton(new PokemonSpriteDialogService());
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IJSRuntime, SettingsJsRuntime>();
        services.AddSingleton<IStringLocalizer<TrackerResources>, CandidateLocalizer>();
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = renderer.BeginRenderingComponent<EvolutionCandidateList>(Parameters(_speciesId));
            TrackerMessage first = await stream.ReadRequestAsync();
            Complete(session, first, _firstName);
            await view.QuiescenceTask;
            Assert.Contains(_firstName, view.ToHtmlString());
            Assert.Contains("aria-busy=\"false\"", view.ToHtmlString());
            await check(activator.Component!, () => System.Net.WebUtility.HtmlDecode(view.ToHtmlString()), session, stream);
        });
    }

    /// <summary>
    /// Builds the required archived list inputs without a native generation recipe.
    /// </summary>
    /// <param name="speciesId">The source species.</param>
    /// <returns>The component parameters.</returns>
    private static ParameterView Parameters(string speciesId) => ParameterView.FromDictionary(new Dictionary<string, object?>
    {
        [nameof(EvolutionCandidateList.SpeciesId)] = speciesId,
        [nameof(EvolutionCandidateList.Heading)] = _runId,
        [nameof(EvolutionCandidateList.AriaLabel)] = _runId,
        [nameof(EvolutionCandidateList.Side)] = EvolutionCandidateSide.Head,
        [nameof(EvolutionCandidateList.Recipe)] = new CompletedRunRecipePayload { RunId = _runId, Result = _runId, GenerationProfileId = _runId, GameVersion = _runId, IronmonVersion = _runId, Configuration = new() { WildPolicy = _runId, TrainerPolicy = _runId, UnfusionSetting = _runId }, SpeciesGenerator = new() { PoolFingerprint = _runId }, AbilityGenerator = new() { PoolFingerprint = _runId }, PlayerFusionGenerator = new() { PoolFingerprint = _runId } }
    });

    /// <summary>
    /// Completes one request with a full page whose obtainability is still being calculated.
    /// </summary>
    /// <param name="session">The pending request session.</param>
    /// <param name="request">The request being answered.</param>
    /// <param name="name">The visible name identifying the response.</param>
    /// <returns>Whether the response still belongs to a pending request.</returns>
    private static bool Complete(TrackerRequestSession session, TrackerMessage request, string name)
    {
        EvolutionCandidateSearchResponsePayload page = new()
        {
            Total = 36,
            Matches = [.. Enumerable.Range(0, 12).Select(index => new EvolutionCandidateSnapshot { SpeciesId = $"{_speciesId}{index}", SpeciesName = name, ObtainabilityStatus = PokemonObtainabilityStatus.Calculating })]
        };

        return session.TryComplete(TrackerMessageFactory.CreateResponse(request.RequestId!, page, _runId));
    }

    /// <summary>
    /// Starts a private interaction without waiting for its protocol response.
    /// </summary>
    /// <param name="component">The real candidate list.</param>
    /// <param name="method">The interaction method name.</param>
    /// <returns>The pending interaction.</returns>
    private static Task Interact(EvolutionCandidateList component, string method)
    {
        Task pending = (Task)typeof(EvolutionCandidateList).GetMethod(method, _privateMembers)!.Invoke(component, null)!;
        Render(component);
        return pending;
    }

    /// <summary>
    /// Renders the current component state after a directly invoked interaction.
    /// </summary>
    /// <param name="component">The component to refresh.</param>
    private static void Render(EvolutionCandidateList component)
        => typeof(ComponentBase).GetMethod(_render, _privateMembers)!.Invoke(component, null);

    /// <summary>
    /// Captures requests without responding until the test explicitly completes or disconnects them.
    /// </summary>
    private sealed class CandidateStream : MemoryStream
    {
        private readonly Channel<TrackerMessage> _requests = Channel.CreateUnbounded<TrackerMessage>();

        /// <summary>
        /// Captures a complete protocol message written by the real request client.
        /// </summary>
        /// <param name="buffer">The encoded message bytes.</param>
        /// <param name="cancellationToken">The write cancellation token.</param>
        /// <returns>A task representing capture.</returns>
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
        /// Waits for the next outgoing request with a bounded test deadline.
        /// </summary>
        /// <returns>The next request.</returns>
        internal async Task<TrackerMessage> ReadRequestAsync() => await _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// Captures the candidate list while creating its ordinary production children.
    /// </summary>
    private sealed class CandidateActivator : IComponentActivator
    {
        /// <summary>
        /// Gets the rendered candidate list.
        /// </summary>
        internal EvolutionCandidateList? Component { get; private set; }

        /// <summary>
        /// Creates a component requested by the renderer.
        /// </summary>
        /// <param name="componentType">The requested type.</param>
        /// <returns>The new component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is EvolutionCandidateList candidate)
                Component = candidate;

            return component;
        }
    }

    /// <summary>
    /// Reads production localization without starting the desktop application.
    /// </summary>
    private sealed class CandidateLocalizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets text for a resource key.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets formatted resource text.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Supplies the unused enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether parent resources are included.</param>
        /// <returns>An empty sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
