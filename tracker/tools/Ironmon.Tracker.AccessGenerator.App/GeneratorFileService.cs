using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Ironmon.Tracker.AccessGenerator.App;

/// <summary>
/// Loads signing keys and saves access tokens through owned native Windows pickers.
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
    public Task<string?> SaveTokenAsync(string suggestedFileName, string token)
        => MainThread.InvokeOnMainThreadAsync(() => SaveTokenOnMainThreadAsync(suggestedFileName, token));

    /// <summary>
    /// Opens the native signing-key picker on the UI thread and imports a bounded PEM file.
    /// </summary>
    /// <returns>The imported key, or null when selection is cancelled.</returns>
    public Task<DiagnosticAccessSigningKey?> LoadSigningKeyAsync()
        => MainThread.InvokeOnMainThreadAsync(LoadSigningKeyOnMainThreadAsync);

    /// <summary>
    /// Selects and validates a private key without involving the embedded browser file chooser.
    /// </summary>
    /// <returns>The imported key, or null when selection is cancelled.</returns>
    private async Task<DiagnosticAccessSigningKey?> LoadSigningKeyOnMainThreadAsync()
    {
        FileOpenPicker picker = new() { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(GeneratorApplicationConstants.PrivateKeyFileExtension);
        InitializeWithWindow.Initialize(picker, GetOwnerWindowHandle());
        StorageFile? file = await picker.PickSingleFileAsync();
        if (file is null)
            return null;

        await using Stream stream = await file.OpenStreamForReadAsync();
        using StreamReader reader = new(stream);
        char[] buffer = new char[DiagnosticAccessGeneratorConstants.MaximumPrivateKeyCharacters + 1];
        try
        {
            int charactersRead = await reader.ReadBlockAsync(buffer.AsMemory());
            return DiagnosticAccessSigningKey.Import(new string(buffer, 0, charactersRead), file.Name);
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    /// <summary>
    /// Gets the native owner for file pickers while executing on the UI thread.
    /// </summary>
    /// <returns>The generator window handle.</returns>
    private nint GetOwnerWindowHandle()
    {
        Application application = Application.Current ?? throw new InvalidOperationException(_text["Generator.Errors.WindowUnavailable"]);
        Window window = application.Windows.FirstOrDefault() ?? throw new InvalidOperationException(_text["Generator.Errors.WindowUnavailable"]);
        Microsoft.UI.Xaml.Window nativeWindow = window.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException(_text["Generator.Errors.NativeWindowUnavailable"]);

        return WindowNative.GetWindowHandle(nativeWindow);
    }

    /// <summary>
    /// Selects a destination and writes the token from the native UI thread.
    /// </summary>
    /// <param name="suggestedFileName">The suggested token filename.</param>
    /// <param name="token">The compact access token.</param>
    /// <returns>The saved path, or null when selection is cancelled.</returns>
    private async Task<string?> SaveTokenOnMainThreadAsync(string suggestedFileName, string token)
    {
        FileSavePicker picker = new()
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };

        picker.FileTypeChoices.Add(_text["Generator.FilePicker.AccessTokenType"], [DiagnosticAccessGeneratorConstants.TokenFileExtension]);
        InitializeWithWindow.Initialize(picker, GetOwnerWindowHandle());
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null)
            return null;

        await FileIO.WriteTextAsync(file, token);
        return file.Path;
    }
}
