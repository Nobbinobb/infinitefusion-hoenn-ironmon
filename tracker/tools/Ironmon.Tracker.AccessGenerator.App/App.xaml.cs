namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Owns the access-token generator application lifetime and main window.
/// </summary>
public partial class App : Application
{
    private readonly IStringLocalizer<GeneratorResources> _text;

    /// <summary>
    /// Initializes the access-token generator application.
    /// </summary>
    /// <param name="text">The localized generator text.</param>
    public App(IStringLocalizer<GeneratorResources> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
        InitializeComponent();
    }

    /// <summary>
    /// Creates the resizable access-token generator window.
    /// </summary>
    /// <param name="activationState">The platform activation state when available.</param>
    /// <returns>The configured generator window.</returns>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new(new MainPage())
        {
            Title = _text["App.Native.AppTitle"],
            Width = GeneratorApplicationConstants.DefaultWindowWidth,
            Height = GeneratorApplicationConstants.DefaultWindowHeight,
            MinimumWidth = GeneratorApplicationConstants.MinimumWindowWidth,
            MinimumHeight = GeneratorApplicationConstants.MinimumWindowHeight
        };
    }
}
