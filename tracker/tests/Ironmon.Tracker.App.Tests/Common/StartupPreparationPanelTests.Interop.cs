using Ironmon.Tracker.App.Components.Common;
using Ironmon.Tracker.Connection.Transport;
using Ironmon.Tracker.Protocol.Connection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Tests.Common;

/// <summary>
/// Exercises native-dialog lifecycle reconciliation without controlling an actual app or browser.
/// </summary>
public sealed partial class StartupPreparationPanelTests
{
    private const string _afterRender = "OnAfterRenderAsync";
    private const string _showFunction = "ironmonStartup.show";
    private const string _closeFunction = "ironmonStartup.close";
    private const string _previewRootVariable = "IRONMON_STARTUP_UI_PREVIEW_ROOT";
    private const string _previewName = "preview.html";

    /// <summary>
    /// Opens the native top-layer dialog once, retains it across phase/failure updates and closes on the actual failure action.
    /// </summary>
    /// <returns>Completion after checking real component lifecycle interop calls and producing optional actual markup.</returns>
    [Fact]
    public async Task NativeDialogOpensOnceAndClosesAfterFailureBackAction()
    {
        await WithInteropAsync(async (state, panel, javaScript, markup) =>
        {
            await AfterRenderAsync(panel);
            Assert.Empty(javaScript.Calls);
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            await AfterRenderAsync(panel);
            Assert.Equal([_showFunction], javaScript.Calls);
            Assert.IsType<ElementReference>(Assert.Single(javaScript.Arguments[0]));

            string? previewRoot = Environment.GetEnvironmentVariable(_previewRootVariable);
            if (!string.IsNullOrWhiteSpace(previewRoot))
            {
                Directory.CreateDirectory(previewRoot);
                await File.WriteAllTextAsync(Path.Combine(previewRoot, _previewName), markup());
            }

            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _worldPhase);
            await AfterRenderAsync(panel);
            Assert.Equal([_showFunction], javaScript.Calls);
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.FailedStatus, _worldPhase);
            await AfterRenderAsync(panel);
            Assert.Equal([_showFunction], javaScript.Calls);
            Navigate(panel, _dismissFailure);
            await AfterRenderAsync(panel);
            Assert.Equal([_showFunction, _closeFunction], javaScript.Calls);
            AssertWaitingHidden(markup());
        });
    }

    /// <summary>
    /// Reconciles completion received while showModal is pending so an empty top-layer dialog cannot remain stuck open.
    /// </summary>
    /// <returns>Completion after the delayed browser response and required close have both been observed.</returns>
    [Fact]
    public async Task CompletionDuringPendingShowStillClosesNativeDialog()
    {
        await WithInteropAsync(async (state, panel, javaScript, markup) =>
        {
            javaScript.DelayShow = true;
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.RunningStatus, _loadingPhase);
            Task showing = AfterRenderAsync(panel);
            Assert.Equal([_showFunction], javaScript.Calls);
            Observe(state, _firstPreparation, StartupPreparationProgressPayload.CompleteStatus, _loadingPhase);
            await AfterRenderAsync(panel);
            javaScript.CompleteShow();
            await showing;
            await AfterRenderAsync(panel);
            Assert.Equal([_showFunction, _closeFunction], javaScript.Calls);
            AssertWaitingHidden(markup());
        });
    }

    /// <summary>
    /// Invokes the production interactive-render lifecycle, which static HtmlRenderer intentionally does not call.
    /// </summary>
    /// <param name="panel">The rendered production loading component.</param>
    /// <returns>The actual lifecycle operation, including awaited browser interop.</returns>
    private static Task AfterRenderAsync(StartupPreparationPanel panel)
        => (Task)typeof(StartupPreparationPanel).GetMethod(_afterRender, _members)!.Invoke(panel, new object[] { false })!;

    /// <summary>
    /// Renders the real loading component and icons with a controlled browser interop boundary.
    /// </summary>
    /// <param name="check">The state and interop transitions to exercise.</param>
    /// <returns>Completion after the real component and all subscriptions have been disposed.</returns>
    private static async Task WithInteropAsync(Func<TrackerConnectionState, StartupPreparationPanel, StartupJavaScript, Func<string>, Task> check)
    {
        TrackerConnectionState state = new();
        HomeActivator activator = new();
        StartupJavaScript javaScript = new();
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(state);
        services.AddSingleton<IComponentActivator>(activator);
        services.AddSingleton<IStringLocalizer<TrackerResources>, StartupLocalizer>();
        services.AddSingleton<IJSRuntime>(javaScript);
        await using ServiceProvider provider = services.BuildServiceProvider();
        await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<StartupPreparationPanel>(ParameterView.Empty);
            await check(state, activator.Panel!, javaScript, rendered.ToHtmlString);
        });
    }

    /// <summary>
    /// Records actual interop requests and can defer showModal to reproduce an event arriving during browser work.
    /// </summary>
    private sealed class StartupJavaScript : IJSRuntime
    {
        private readonly TaskCompletionSource _showCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Gets native dialog operations requested by the production component.
        /// </summary>
        internal List<string> Calls { get; } = [];

        /// <summary>
        /// Gets the real element reference arguments supplied to each browser request.
        /// </summary>
        internal List<object?[]> Arguments { get; } = [];

        /// <summary>
        /// Gets or sets whether the next show request must wait for the test's completion signal.
        /// </summary>
        internal bool DelayShow { get; set; }

        /// <summary>
        /// Completes the delayed native show operation.
        /// </summary>
        internal void CompleteShow()
            => _showCompletion.TrySetResult();

        /// <summary>
        /// Captures a native-dialog browser operation without controlling any browser surface.
        /// </summary>
        /// <typeparam name="TValue">The interop result type.</typeparam>
        /// <param name="identifier">The actual JavaScript operation identifier.</param>
        /// <param name="args">The production element reference arguments.</param>
        /// <returns>The controlled browser completion.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        /// <summary>
        /// Captures a cancelable native-dialog operation and optionally delays show completion.
        /// </summary>
        /// <typeparam name="TValue">The interop result type.</typeparam>
        /// <param name="identifier">The actual JavaScript operation identifier.</param>
        /// <param name="cancellationToken">The browser request cancellation token.</param>
        /// <param name="args">The production element reference arguments.</param>
        /// <returns>Completion after the controlled browser operation finishes.</returns>
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls.Add(identifier);
            Arguments.Add(args ?? []);
            if (identifier == _showFunction && DelayShow)
                await _showCompletion.Task.WaitAsync(cancellationToken);

            return default!;
        }
    }
}
