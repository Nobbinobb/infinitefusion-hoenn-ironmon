using Microsoft.JSInterop;

namespace Ironmon.Tracker.App.Tests.Settings;

/// <summary>
/// Supplies the browser dependency for static settings component tests.
/// </summary>
internal sealed class SettingsJsRuntime : IJSRuntime
{
    /// <summary>
    /// Completes browser-only operations without accessing desktop UI.
    /// </summary>
    /// <typeparam name="TValue">The expected browser result type.</typeparam>
    /// <param name="identifier">The browser function identifier.</param>
    /// <param name="args">The browser function arguments.</param>
    /// <returns>The default browser result.</returns>
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

    /// <summary>
    /// Completes cancellable browser-only operations without accessing desktop UI.
    /// </summary>
    /// <typeparam name="TValue">The expected browser result type.</typeparam>
    /// <param name="identifier">The browser function identifier.</param>
    /// <param name="cancellationToken">The browser operation's cancellation token.</param>
    /// <param name="args">The browser function arguments.</param>
    /// <returns>The default browser result.</returns>
    public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => ValueTask.FromResult(default(TValue)!);
}
