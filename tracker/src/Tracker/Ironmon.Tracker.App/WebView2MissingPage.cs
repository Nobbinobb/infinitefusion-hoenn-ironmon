namespace Ironmon.Tracker.App;

/// <summary>
/// Presents recovery guidance when Microsoft Edge WebView2 is unavailable.
/// </summary>
internal sealed class WebView2MissingPage : ContentPage
{
    private static readonly Uri _downloadPage = new(TrackerApplicationConstants.WebView2DownloadUrl);

    /// <summary>
    /// Initializes the WebView2 recovery page.
    /// </summary>
    /// <param name="text">The localized tracker text.</param>
    internal WebView2MissingPage(IStringLocalizer<TrackerResources> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Title = text["App.WebView.AppTitle"];
        BackgroundColor = Color.FromArgb("#16131F");
        Label heading = new()
        {
            Text = text["App.WebView.WebView2RequiredHeading"],
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };
        Label description = new()
        {
            Text = text["App.WebView.WebView2RequiredDescription"],
            FontSize = 15,
            TextColor = Color.FromArgb("#C6BED8")
        };
        Button installButton = new()
        {
            Text = text["App.WebView.WebView2DownloadButton"],
            BackgroundColor = Color.FromArgb("#7257D9"),
            TextColor = Colors.White
        };
        installButton.Clicked += OpenDownloadPage;
        Content = new VerticalStackLayout
        {
            Padding = new Thickness(32),
            Spacing = 18,
            VerticalOptions = LayoutOptions.Center,
            Children = { heading, description, installButton }
        };
    }

    /// <summary>
    /// Opens Microsoft's WebView2 Runtime download page.
    /// </summary>
    /// <param name="sender">The recovery button.</param>
    /// <param name="args">The click event arguments.</param>
    private async void OpenDownloadPage(object? sender, EventArgs args)
        => await Launcher.Default.OpenAsync(_downloadPage);
}
