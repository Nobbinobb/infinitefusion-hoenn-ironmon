using System.Text.Json;
using System.Text.Json.Serialization;
using Ironmon.Updater.Infrastructure;

namespace Ironmon.Tracker.App;

/// <summary>
/// Carries bounded navigation identities across updater relaunch without persisting live gameplay payloads.
/// </summary>
public sealed class TrackerUpdateNavigation
{
    private const string ResumeArgument = "--resume-update";
    private const string ScrollKey = "scroll";
    private static readonly JsonSerializerOptions _options = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12 };
    private readonly Dictionary<string, Func<JsonElement>> _captures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonElement> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets whether an update panel currently protects the originating navigation from automatic tab switches.
    /// </summary>
    public bool IsUpdateOpen { get; set; }

    /// <summary>
    /// Gets or sets the renderer-owned Settings entry into a fresh update check.
    /// </summary>
    public Func<Task>? OpenUpdates { get; set; }

    /// <summary>
    /// Gets or sets the renderer-owned capture callback used immediately before handoff.
    /// </summary>
    public Func<Task<string>>? Capture { get; set; }

    /// <summary>
    /// Reads a matching bounded resume record while treating absent or malformed navigation as a normal startup.
    /// </summary>
    public TrackerUpdateNavigation()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, ResumeArgument);
        if (index < 0 || index + 1 >= arguments.Length || !Guid.TryParse(arguments[index + 1], out var id))
            return;

        try
        {
            var root = GameInstallationLocator.FindFromTracker(Environment.ProcessPath ?? string.Empty);
            Import(TrackerRelaunch.ReadNavigation(root, id));
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException or InvalidOperationException)
        {
            _pending.Clear();
        }
    }

    /// <summary>
    /// Registers only an explicit typed navigation capture for a mounted component.
    /// </summary>
    /// <typeparam name="T">The bounded navigation record type.</typeparam>
    /// <param name="key">The component-owned identity.</param>
    /// <param name="capture">The selection capture, without cached gameplay details.</param>
    public void Register<T>(string key, Func<T> capture) => _captures[key] = ()
        => JsonSerializer.SerializeToElement(capture(), _options);

    /// <summary>
    /// Releases a component's capture when its ordinary navigation surface is unmounted.
    /// </summary>
    /// <param name="key">The component-owned identity.</param>
    public void Unregister(string key)
        => _captures.Remove(key);

    /// <summary>
    /// Consumes one typed resume selection; components still validate IDs, scope and enums against their normal data.
    /// </summary>
    /// <typeparam name="T">The component-owned navigation record.</typeparam>
    /// <param name="key">The component identity.</param>
    /// <returns>The saved navigation or the default when it is absent or malformed.</returns>
    public T? Take<T>(string key)
    {
        if (!_pending.Remove(key, out var value))
            return default;

        try
        {
            return value.Deserialize<T>(_options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Exports mounted navigation plus bounded browser scroll coordinates for the acknowledged handoff.
    /// </summary>
    /// <param name="scroll">The browser's structural scroll positions, containing no executable commands.</param>
    /// <returns>At most 4096 characters of navigation data.</returns>
    public string Export(JsonElement scroll)
    {
        var values = _captures.ToDictionary(pair => pair.Key, pair => pair.Value(), StringComparer.Ordinal);
        values[ScrollKey] = scroll;
        var json = JsonSerializer.Serialize(values, _options);
        if (json.Length > 4096)
            throw new InvalidOperationException("The current navigation is too large to preserve. Return to the main tracker tab before updating.");

        return json;
    }

    /// <summary>
    /// Validates a bounded opaque record without interpreting it as routes, script or file paths.
    /// </summary>
    /// <param name="json">The exact resume data.</param>
    public void Import(string json)
    {
        if (json.Length > 4096)
            throw new InvalidDataException("The update navigation exceeds its supported size.");

        var values = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, _options) ?? throw new InvalidDataException("The update navigation is empty.");
        if (values.Count > 20 || values.Keys.Any(key => key.Length > 128))
            throw new InvalidDataException("The update navigation contains too many selections.");

        _pending.Clear();
        foreach (var pair in values)
            _pending.Add(pair.Key, pair.Value);
    }

    /// <summary>
    /// Consumes the bounded scroll record for restoration as asynchronously loaded views become available.
    /// </summary>
    /// <returns>The browser scroll observation, or an undefined JSON element.</returns>
    public JsonElement TakeScroll()
        => Take<JsonElement>(ScrollKey);
}
