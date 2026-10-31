using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.Extensions.FileProviders;

namespace Ironmon.Tracker.App.Tests.Packaging;

/// <summary>
/// Verifies startup presentation assets are delivered through the desktop WebView's real content resolver.
/// </summary>
public sealed class TrackerStylesheetPackagingTests
{
    private const string _webRoot = "wwwroot";
    private const string _hostPage = "index.html";
    private const string _startupStylesheet = "obsidian-startup.css";
    private const string _componentStylesheet = "tracker-components.styles.css";
    private const string _stylesheetLinkTemplate = "<link rel=\"stylesheet\" href=\"{0}\" />";
    private const string _origin = "https://0.0.0.0/";
    private const string _selector = ".startup-preparation";
    private const string _scopedSelector = ".startup-preparation[b-";
    private const string _contentType = "Content-Type";
    private const string _cssContentType = "text/css";

    /// <summary>
    /// Verifies the shipped host page loads URL-safe startup and component CSS through the native desktop content pipeline.
    /// </summary>
    /// <param name="stylesheetName">The delivered startup or component stylesheet filename.</param>
    /// <returns>Completion after reading the published web-root assets without creating a browser or native window.</returns>
    [Theory]
    [InlineData(_startupStylesheet)]
    [InlineData(_componentStylesheet)]
    public async Task StartupAndComponentStylesAreLinkedAndServedByDesktopWebView(string stylesheetName)
    {
        string root = Path.Combine(AppContext.BaseDirectory, _webRoot);
        string host = await File.ReadAllTextAsync(Path.Combine(root, _hostPage));
        byte[] stylesheet = await File.ReadAllBytesAsync(Path.Combine(root, stylesheetName));
        string css = System.Text.Encoding.UTF8.GetString(stylesheet);
        string stylesheetLink = string.Format(_stylesheetLinkTemplate, stylesheetName);
        Assert.Contains(stylesheetLink, host);
        Assert.Single(host.Split(stylesheetLink, StringSplitOptions.None).Skip(1));
        Assert.DoesNotContain(' ', stylesheetName);
        if (stylesheetName == _startupStylesheet)
        {
            Assert.Contains(_selector, css);
            Assert.DoesNotContain(_scopedSelector, css);
        }

        await using ServiceProvider services = new ServiceCollection().AddLogging().BuildServiceProvider();
        using PhysicalFileProvider files = new(root);
        await using ContentWebViewManager manager = new(services, files);
        Assert.True(manager.TryRead(new Uri(new Uri(_origin), stylesheetName).AbsoluteUri, out int status, out Stream content, out IDictionary<string, string> headers));
        await using (content)
        {
            Assert.Equal(200, status);
            Assert.StartsWith(_cssContentType, headers[_contentType], StringComparison.OrdinalIgnoreCase);
            using MemoryStream received = new();
            await content.CopyToAsync(received);
            Assert.Equal(stylesheet, received.ToArray());
        }
    }

    /// <summary>
    /// Exercises content handling without providing navigation or JavaScript transports.
    /// </summary>
    private sealed class ContentWebViewManager : WebViewManager
    {
        /// <summary>
        /// Initializes the desktop content pipeline with only its shipped web root.
        /// </summary>
        /// <param name="services">The service provider available to the content pipeline.</param>
        /// <param name="files">The shipped static asset provider.</param>
        public ContentWebViewManager(IServiceProvider services, IFileProvider files) : base(services, Microsoft.AspNetCore.Components.Dispatcher.CreateDefault(), new Uri(_origin), files, new JSComponentConfigurationStore(), _hostPage)
        {
        }

        /// <summary>
        /// Resolves one asset using the same implementation as the native tracker.
        /// </summary>
        /// <param name="uri">The absolute asset request URI.</param>
        /// <param name="status">The returned HTTP status.</param>
        /// <param name="content">The response stream owned by the caller.</param>
        /// <param name="headers">The static response headers supplied by the desktop content pipeline.</param>
        /// <returns>Whether the tracker content pipeline handled the request.</returns>
        public bool TryRead(string uri, out int status, out Stream content, out IDictionary<string, string> headers)
            => TryGetResponseContent(uri, false, out status, out _, out content, out headers);

        /// <summary>
        /// Rejects navigation because this fixture only resolves static content.
        /// </summary>
        /// <param name="absoluteUri">The unexpected navigation destination.</param>
        protected override void NavigateCore(Uri absoluteUri)
            => throw new InvalidOperationException("The stylesheet fixture cannot navigate.");

        /// <summary>
        /// Rejects JavaScript transport because this fixture does not create a browser.
        /// </summary>
        /// <param name="message">The unexpected transport message.</param>
        protected override void SendMessage(string message)
            => throw new InvalidOperationException("The stylesheet fixture cannot send browser messages.");
    }
}
