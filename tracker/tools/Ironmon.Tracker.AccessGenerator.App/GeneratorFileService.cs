using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Saves generated access tokens through the native Windows file picker.
/// </summary>
public sealed class GeneratorFileService
{
    private readonly IStringLocalizer<GeneratorResources> _text;

    /// <summary>
    /// Initializes the generator file service.
    /// </summary>
    /// <param name="text">The localized generator text.</param>
    public GeneratorFileService(IStringLocalizer<GeneratorResources> text)
    {
        ArgumentNullException.ThrowIfNull(text);
        _text = text;
    }

    /// <summary>
    /// Prompts for a destination and writes one generated access token.
    /// </summary>
    /// <param name="suggestedFileName">The suggested token filename.</param>
    /// <param name="token">The compact access token.</param>
    /// <returns>The saved path, or null when the picker was cancelled.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no native generator window is available.</exception>
    public async Task<string?> SaveTokenAsync(string suggestedFileName, string token)
    {
        Application application = Application.Current ?? throw new InvalidOperationException(_text["Generator.Errors.WindowUnavailable"]);
        Window window = application.Windows.FirstOrDefault() ?? throw new InvalidOperationException(_text["Generator.Errors.WindowUnavailable"]);
        Microsoft.UI.Xaml.Window nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException(_text["Generator.Errors.NativeWindowUnavailable"]);

        FileSavePicker picker = new()
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        picker.FileTypeChoices.Add(_text["Generator.FilePicker.AccessTokenType"], [DiagnosticAccessGeneratorConstants.TokenFileExtension]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(nativeWindow));
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
            return null;

        await FileIO.WriteTextAsync(file, token);
        return file.Path;
    }
}
