using Markdig;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace Ironmon.Tracker.App.Components.Updates;

/// <summary>
/// Renders authenticated Markdown release notes inside the tracker update dialog.
/// </summary>
public partial class TrackerReleaseNotes
{
    private const string TargetAttribute = "target";
    private const string ExternalTarget = "_blank";
    private const string RelAttribute = "rel";
    private const string ExternalRel = "noopener noreferrer";
    private static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseAutoLinks()
        .UseAlertBlocks()
        .UsePipeTables()
        .Build();
    private string? _lastNotes;
    private MarkupString _renderedNotes;

    /// <summary>
    /// Gets or sets the authenticated release notes to display inside the update dialog.
    /// </summary>
    [Parameter, EditorRequired]
    public string MarkdownText { get; set; } = string.Empty;

    /// <summary>
    /// Renders new notes while retaining the markup across progress and consent updates.
    /// </summary>
    protected override void OnParametersSet()
    {
        if (MarkdownText == _lastNotes)
            return;

        var document = Markdown.Parse(MarkdownText, _pipeline);
        foreach (var heading in document.Descendants<HeadingBlock>())
            heading.Level = Math.Min(6, heading.Level + 2);

        foreach (var link in document.Descendants<LinkInline>().ToArray())
        {
            if (link.IsImage || !IsWebAddress(link.GetDynamicUrl?.Invoke() ?? link.Url))
            {
                link.ReplaceBy(new LiteralInline(string.Empty));
            }
            else
            {
                OpenExternally(link);
            }
        }

        foreach (var link in document.Descendants<AutolinkInline>().ToArray())
        {
            if (link.IsEmail || !IsWebAddress(link.Url))
            {
                link.ReplaceBy(new LiteralInline(link.Url));
            }
            else
            {
                OpenExternally(link);
            }
        }

        _renderedNotes = new MarkupString(Markdown.ToHtml(document, _pipeline));
        _lastNotes = MarkdownText;
    }

    /// <summary>
    /// Checks whether a link has an absolute HTTP or HTTPS destination.
    /// </summary>
    /// <param name="address">The parsed Markdown destination, if present.</param>
    /// <returns>Whether the address is suitable for opening in the browser.</returns>
    private static bool IsWebAddress(string? address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Host.Length != 0 && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>
    /// Directs a verified web link to the default browser outside the tracker WebView.
    /// </summary>
    /// <param name="link">The Markdown link with a verified destination.</param>
    private static void OpenExternally(Inline link)
    {
        var attributes = link.GetAttributes();
        attributes.AddPropertyIfNotExist(TargetAttribute, ExternalTarget);
        attributes.AddPropertyIfNotExist(RelAttribute, ExternalRel);
    }
}
