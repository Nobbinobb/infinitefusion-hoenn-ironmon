using System.Globalization;
using System.Runtime.InteropServices;

namespace Ironmon.Tracker.AccessGenerator.App.Components;

/// <summary>
/// Coordinates the access-token generator form and native output actions.
/// </summary>
public partial class GeneratorPage : ComponentBase, IDisposable
{
    private readonly DiagnosticAccessSelection _selection = new();
    private DiagnosticAccessSigningKey? _signingKey;
    private DiagnosticAccessTokenGenerationResult? _result;
    private string _tokenId = CreateTokenId();
    private string _presetId = string.Empty;
    private string _presetDescription = string.Empty;
    private string _note = string.Empty;
    private string _expirationLocal = DateTime.Now.AddDays(GeneratorApplicationConstants.DefaultExpirationDays).ToString(GeneratorApplicationConstants.LocalDateTimeInputFormat, CultureInfo.InvariantCulture);
    private bool _neverExpires;
    private string? _errorMessage;
    private string? _resultMessage;

    /// <summary>
    /// Loads and validates the selected external private key.
    /// </summary>
    /// <param name="args">The file-selection event.</param>
    private async Task LoadSigningKeyAsync(InputFileChangeEventArgs args)
    {
        ClearMessages();
        _signingKey?.Dispose();
        _signingKey = null;
        _result = null;
        try
        {
            IBrowserFile file = args.File;
            await using Stream stream = file.OpenReadStream(DiagnosticAccessGeneratorConstants.MaximumPrivateKeyCharacters);
            using StreamReader reader = new(stream);
            string pem = await reader.ReadToEndAsync();
            _signingKey = DiagnosticAccessSigningKey.Import(pem, file.Name);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException)
        {
            _errorMessage = exception.Message;
        }
    }

    /// <summary>
    /// Applies a local preset as an editable direct selection.
    /// </summary>
    private void ApplySelectedPreset()
    {
        DiagnosticAccessPreset? preset = DiagnosticAccessPresetCatalog.All.FirstOrDefault(candidate => candidate.Id == _presetId);
        if (preset is null)
        {
            _presetDescription = string.Empty;
            return;
        }

        _selection.Replace(preset.Capabilities);
        _presetDescription = Text[GeneratorLocalizationKeys.GetPresetDescription(preset.Id)];
        InvalidateResult();
    }

    /// <summary>
    /// Updates one direct capability checkbox.
    /// </summary>
    /// <param name="capability">The stable capability identifier.</param>
    /// <param name="selected">Whether the capability should be selected.</param>
    private void SetCapability(string capability, bool selected)
    {
        _selection.Set(capability, selected);
        _presetId = string.Empty;
        _presetDescription = string.Empty;
        InvalidateResult();
    }

    /// <summary>
    /// Updates the optional support note.
    /// </summary>
    /// <param name="args">The text-input event.</param>
    private void UpdateNote(ChangeEventArgs args)
    {
        _note = args.Value?.ToString() ?? string.Empty;
        InvalidateResult();
    }

    /// <summary>
    /// Toggles lifetime access for the generated token.
    /// </summary>
    /// <param name="args">The checkbox change event.</param>
    private void SetNeverExpires(ChangeEventArgs args)
    {
        _neverExpires = args.Value is true;
        InvalidateResult();
    }

    /// <summary>
    /// Updates the local expiration input.
    /// </summary>
    /// <param name="args">The date-input event.</param>
    private void UpdateExpiration(ChangeEventArgs args)
    {
        _expirationLocal = args.Value?.ToString() ?? string.Empty;
        InvalidateResult();
    }

    /// <summary>
    /// Generates a signed JWT from the currently reviewed form state.
    /// </summary>
    private void GenerateToken()
    {
        ClearMessages();
        try
        {
            DiagnosticAccessSigningKey key = _signingKey ?? throw new InvalidOperationException(Text["Generator.Errors.SelectSigningKey"]);
            DateTimeOffset issuedAt = DateTimeOffset.UtcNow;
            DateTimeOffset? expiresAt = GetExpiration();
            DiagnosticAccessTokenGenerationRequest request = new(_tokenId, issuedAt, expiresAt, NormalizeNote(), _selection.DirectCapabilities);
            _result = TokenGenerator.Generate(key, request);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException)
        {
            _errorMessage = exception.Message;
        }
    }

    /// <summary>
    /// Copies the generated compact token to the operating-system clipboard.
    /// </summary>
    private async Task CopyTokenAsync()
    {
        if (_result is null)
            return;

        ClearMessages();
        try
        {
            await Clipboard.Default.SetTextAsync(_result.Token);
            _resultMessage = Text["Generator.Messages.TokenCopied"];
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or UnauthorizedAccessException or COMException)
        {
            _errorMessage = Text["Generator.Errors.CopyFailed"];
        }
    }

    /// <summary>
    /// Saves the generated compact token through the native file picker.
    /// </summary>
    private async Task SaveTokenAsync()
    {
        if (_result is null)
            return;

        ClearMessages();
        try
        {
            string filename = $"{GeneratorApplicationConstants.TokenFileNamePrefix}{_result.TokenId}{DiagnosticAccessGeneratorConstants.TokenFileExtension}";
            string? path = await FileService.SaveTokenAsync(filename, _result.Token);
            _resultMessage = path is null ? Text["Generator.Messages.SaveCancelled"] : Text["Generator.Messages.TokenSaved", path];
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or COMException)
        {
            _errorMessage = Text["Generator.Errors.SaveFailed"];
        }
    }

    /// <summary>
    /// Prepares a unique identifier for another token while retaining the form choices.
    /// </summary>
    private void StartNewToken()
    {
        _tokenId = CreateTokenId();
        _result = null;
        ClearMessages();
    }

    /// <summary>
    /// Gets the parsed UTC expiration instant for generation.
    /// </summary>
    /// <returns>The UTC expiration, or null for lifetime access.</returns>
    /// <exception cref="FormatException">Thrown when the local expiration input is invalid.</exception>
    private DateTimeOffset? GetExpiration()
    {
        if (_neverExpires)
            return null;

        if (!DateTime.TryParseExact(_expirationLocal, GeneratorApplicationConstants.LocalDateTimeInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime localExpiration))
            throw new FormatException(Text["Generator.Errors.InvalidExpiration"]);

        return new DateTimeOffset(localExpiration).ToUniversalTime();
    }

    /// <summary>
    /// Gets the readable expiration summary shown before generation.
    /// </summary>
    /// <returns>The lifetime or local expiration description.</returns>
    private string GetExpirationSummary()
    {
        if (_neverExpires)
            return Text["Generator.Details.LifetimeAccess"];

        return DateTime.TryParseExact(_expirationLocal, GeneratorApplicationConstants.LocalDateTimeInputFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime expiration)
            ? new DateTimeOffset(expiration).ToUniversalTime().ToString(GeneratorApplicationConstants.UtcExpirationDisplayFormat, CultureInfo.InvariantCulture)
            : Text["Generator.Review.InvalidExpiration"];
    }

    /// <summary>
    /// Normalizes the optional support note for its signed claim.
    /// </summary>
    /// <returns>The trimmed note, or null when empty.</returns>
    private string? NormalizeNote()
        => string.IsNullOrWhiteSpace(_note) ? null : _note.Trim();

    /// <summary>
    /// Invalidates a previously generated token after an input change.
    /// </summary>
    private void InvalidateResult()
    {
        _result = null;
        ClearMessages();
    }

    /// <summary>
    /// Clears transient error and success feedback.
    /// </summary>
    private void ClearMessages()
    {
        _errorMessage = null;
        _resultMessage = null;
    }

    /// <summary>
    /// Creates a compact unique JWT ID.
    /// </summary>
    /// <returns>The lowercase identifier without separators.</returns>
    private static string CreateTokenId()
        => Guid.NewGuid().ToString(GeneratorApplicationConstants.TokenIdFormat);

    /// <summary>
    /// Releases the externally loaded private key when the generator page closes.
    /// </summary>
    public void Dispose()
    {
        _signingKey?.Dispose();
        GC.SuppressFinalize(this);
    }
}
