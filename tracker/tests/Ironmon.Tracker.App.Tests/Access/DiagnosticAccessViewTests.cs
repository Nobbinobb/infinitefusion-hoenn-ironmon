using Ironmon.Tracker.Access;
using Ironmon.Tracker.App.Components.Access;
using Ironmon.Tracker.Connection.Access;
using Ironmon.Tracker.Connection.Knowledge;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Microsoft.JSInterop;
using System.Reflection;
using System.Resources;
using System.Security.Cryptography;

namespace Ironmon.Tracker.App.Tests.Access;

/// <summary>
/// Verifies removal-before-import, access lifecycle rendering, and complete permission labels.
/// </summary>
public sealed class DiagnosticAccessViewTests
{
    private const string _resourceName = "Ironmon.Tracker.App.Resources.Localization.TrackerResources";
    private const string _storageName = "IronmonAccessViewTests";
    private const string _keyId = "access-view-key";
    private const string _unknownCapability = "future.preview_capability";
    private const string _tokenId = "access-view-token";
    private const string _tokenInput = "id=\"diagnostic-access-token\"";
    private const string _activate = "ActivateAsync";
    private const string _update = "UpdateToken";
    private const string _remove = "RemoveAccess";
    private const string _identifiers = "ToggleIdentifiers";
    private const string _snapshotField = "_snapshot";
    private const string _changed = "HandleAccessChanged";
    private const string _render = "StateHasChanged";

    /// <summary>
    /// Prevents replacing stored access through the page and allows activation only after removal.
    /// </summary>
    /// <returns>A task representing activation, rejection, removal, and reactivation.</returns>
    [Fact]
    public async Task StoredAccessMustBeRemovedBeforeAnotherActivation()
    {
        await RenderAsync(false, async (page, html, service, token) =>
        {
            Assert.Contains(_tokenInput, html());
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = "invalid" });
            await InvokeAsync(page, _activate);
            Assert.Contains("malformed", html());
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = token });
            await InvokeAsync(page, _activate);
            Assert.Equal(DiagnosticAccessState.Active, service.Snapshot.State);
            Assert.DoesNotContain(_tokenInput, html());
            Assert.DoesNotContain(_tokenId, html());
            Assert.Contains("Included grants", html());
            Assert.Contains(_unknownCapability, html());
            Assert.DoesNotContain(_unknownCapability, service.Snapshot.EffectiveCapabilities);
            Assert.Contains("Auto-revive", html());
            Assert.DoesNotContain("Access.Capabilities.", html());
            await InvokeAsync(page, _identifiers);
            Assert.Contains(_tokenId, html());
            Assert.DoesNotContain("role=\"dialog\"", html());
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = "invalid" });
            await InvokeAsync(page, _activate);
            Assert.Equal(_tokenId, service.Snapshot.Grant?.TokenId);
            Assert.DoesNotContain("malformed", html());
            await InvokeAsync(page, _remove);
            Assert.Equal(DiagnosticAccessState.None, service.Snapshot.State);
            Assert.Contains(_tokenInput, html());
            Assert.DoesNotContain("Included grants", html());
            await InvokeAsync(page, _update, new ChangeEventArgs { Value = token });
            await InvokeAsync(page, _activate);
            Assert.DoesNotContain(_tokenInput, html());
        });
    }

    /// <summary>
    /// Shows every developer capability without token input or token-removal controls.
    /// </summary>
    /// <returns>A task representing developer-state rendering.</returns>
    [Fact]
    public async Task DeveloperOverrideListsEveryLocalizedPermission()
    {
        await RenderAsync(true, (page, html, service, token) =>
        {
            Assert.DoesNotContain(_tokenInput, html());
            Assert.DoesNotContain("Remove diagnostic access", html());
            Localizer text = new();
            foreach (string capability in DiagnosticCapabilityCatalog.KnownIds)
            {
                string name = text[TrackerDiagnosticAccessLocalizationKeys.GetCapabilityName(capability)];
                Assert.DoesNotContain("Access.Capabilities.", name);
                Assert.Contains(name, System.Net.WebUtility.HtmlDecode(html()));
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Renders unavailable stored access without exposing another import path.
    /// </summary>
    /// <param name="expired">Whether to simulate a token that expired after activation.</param>
    /// <returns>A task representing stored-state rendering and removal.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnavailableStoredAccessStillRequiresRemoval(bool expired)
    {
        await RenderAsync(false, async (page, html, service, token) =>
        {
            await service.ActivateAsync(token);
            DiagnosticAccessSnapshot snapshot = expired
                ? DiagnosticAccessSnapshot.Expired(service.Snapshot.Grant, [], "Expired token.")
                : DiagnosticAccessSnapshot.Invalid(null, "Invalid token.");

            typeof(DiagnosticAccessService).GetField(_snapshotField, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(service, snapshot);
            await InvokeAsync(page, _changed, service, EventArgs.Empty);
            Assert.DoesNotContain(_tokenInput, html());
            Assert.Contains("Remove diagnostic access", html());
            Assert.Contains(expired ? "Diagnostic access expired" : "Stored access is invalid", html());
            if (expired)
            {
                Assert.Contains("Auto-revive", html());
            }
            else
            {
                Assert.DoesNotContain(_tokenId, html());
            }

            await InvokeAsync(page, _remove);
            Assert.Contains(_tokenInput, html());
        });
    }

    /// <summary>
    /// Renders the production page using temporary token storage and an ephemeral signing key.
    /// </summary>
    /// <param name="developer">Whether the isolated service grants local developer access.</param>
    /// <param name="check">Checks executed on the renderer dispatcher.</param>
    /// <returns>A task representing rendering and lifecycle verification.</returns>
    private static async Task RenderAsync(bool developer, Func<DiagnosticAccessPage, Func<string>, DiagnosticAccessService, string, Task> check)
    {
        using ECDsa privateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa publicKey = ECDsa.Create(privateKey.ExportParameters(false));
        ECDsaSecurityKey verificationKey = new(publicKey) { KeyId = _keyId };
        DiagnosticAccessTokenValidator validator = new(new DiagnosticAccessKeyring([verificationKey]));
        string directory = Path.Combine(Path.GetTempPath(), _storageName, Guid.NewGuid().ToString());
        using DiagnosticAccessService access = new(new TrackerKnowledgeOptions(directory), validator, developer);
        string[] capabilities = [.. DiagnosticCapabilityCatalog.KnownIds.Where(id => id != DiagnosticCapabilities.PokemonCurrentPlayer && id != DiagnosticCapabilities.PokemonCurrentEnemies).Append(_unknownCapability).Order(StringComparer.Ordinal)];
        SecurityTokenDescriptor descriptor = new()
        {
            Issuer = DiagnosticAccessTokenConstants.Issuer,
            Audience = DiagnosticAccessTokenConstants.Audience,
            TokenType = DiagnosticAccessTokenConstants.TokenType,
            IssuedAt = DateTime.UtcNow,
            Claims = new Dictionary<string, object>
            {
                [DiagnosticAccessTokenConstants.VersionClaim] = DiagnosticAccessTokenConstants.ContractVersion,
                [JwtRegisteredClaimNames.Jti] = _tokenId,
                [DiagnosticAccessTokenConstants.CapabilitiesClaim] = capabilities
            },
            SigningCredentials = new(new ECDsaSecurityKey(privateKey) { KeyId = _keyId }, SecurityAlgorithms.EcdsaSha256)
        };

        string token = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
        ServiceCollection services = new();
        services.AddLogging();
        services.AddSingleton(access);
        services.AddSingleton<IStringLocalizer<TrackerResources>, Localizer>();
        services.AddSingleton<IJSRuntime, NullRuntime>();
        CapturingActivator<DiagnosticAccessPage> activator = new();
        services.AddSingleton<IComponentActivator>(activator);
        try
        {
            await using ServiceProvider provider = services.BuildServiceProvider();
            await using HtmlRenderer renderer = new(provider, provider.GetRequiredService<ILoggerFactory>());
            await renderer.Dispatcher.InvokeAsync(async () =>
            {
                var view = await renderer.RenderComponentAsync<DiagnosticAccessPage>();
                await check(activator.Component!, view.ToHtmlString, access, token);
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    /// <summary>
    /// Invokes a page handler and requests the resulting render.
    /// </summary>
    /// <param name="component">The production component.</param>
    /// <param name="method">The handler name.</param>
    /// <param name="arguments">The event arguments.</param>
    /// <returns>A task representing the event and render.</returns>
    private static async Task InvokeAsync(IComponent component, string method, params object[] arguments)
    {
        if (component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, arguments) is Task task)
            await task;

        typeof(ComponentBase).GetMethod(_render, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(component, null);
    }

    /// <summary>
    /// Supplies unused browser operations during static component rendering.
    /// </summary>
    private sealed class NullRuntime : IJSRuntime
    {
        /// <summary>
        /// Returns a default result without invoking browser code.
        /// </summary>
        /// <typeparam name="TValue">The requested result type.</typeparam>
        /// <param name="identifier">The browser operation, which is ignored.</param>
        /// <param name="args">The operation arguments, which are ignored.</param>
        /// <returns>A completed value task containing the default result.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => ValueTask.FromResult(default(TValue)!);

        /// <summary>
        /// Delegates to the immediate no-op implementation without observing cancellation.
        /// </summary>
        /// <typeparam name="TValue">The requested result type.</typeparam>
        /// <param name="identifier">The browser operation, which is ignored.</param>
        /// <param name="cancellationToken">The cancellation token, which is ignored.</param>
        /// <param name="args">The operation arguments, which are ignored.</param>
        /// <returns>A completed value task containing the default result.</returns>
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) => InvokeAsync<TValue>(identifier, args);
    }

    /// <summary>
    /// Captures the requested component while allowing shared components to render normally.
    /// </summary>
    /// <typeparam name="T">The component to capture.</typeparam>
    private sealed class CapturingActivator<T> : IComponentActivator where T : IComponent
    {
        /// <summary>
        /// Gets the component created by the renderer.
        /// </summary>
        public T? Component { get; private set; }

        /// <summary>
        /// Creates and optionally captures a component.
        /// </summary>
        /// <param name="componentType">The requested component type.</param>
        /// <returns>The newly created component.</returns>
        public IComponent CreateInstance(Type componentType)
        {
            IComponent component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is T target)
                Component = target;

            return component;
        }
    }

    /// <summary>
    /// Resolves production text without desktop startup.
    /// </summary>
    private sealed class Localizer : IStringLocalizer<TrackerResources>
    {
        private readonly ResourceManager _resources = new(_resourceName, typeof(TrackerResources).Assembly);

        /// <summary>
        /// Gets one production string.
        /// </summary>
        public LocalizedString this[string name] => new(name, _resources.GetString(name) ?? name);

        /// <summary>
        /// Gets one formatted production string.
        /// </summary>
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(this[name].Value, arguments));

        /// <summary>
        /// Supplies the unused enumeration contract.
        /// </summary>
        /// <param name="includeParentCultures">Whether to include parent cultures.</param>
        /// <returns>An empty sequence.</returns>
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
