using System.Globalization;
using System.Text;
using System.Text.Json;
using Ironmon.Tracker.Connection.Transport;

namespace Ironmon.Tracker.Connection.Lookup;

/// <summary>
/// Indexes public authored trainer calls in the connected installation for optional game-owned location hydration.
/// </summary>
/// <remarks>
/// Species mapping and authorization remain in the game. Only ASCII trainer keys use this fast path;
/// unsupported files or peers retain the original Ruby index. Accepted entries are sent once per connection.
/// </remarks>
internal sealed class TrackerTrainerLocationIndex
{
    private const string DataDirectory = "Data";
    private const string MapPattern = "Map???.rxdata";
    private const string TokenFormat = "N";
    private const int MaximumMapFileBytes = 64 * 1024 * 1024;
    private const int RequestEnvelopeAllowance = 1024;
    private const int MaximumIndexBytes = TrackerProtocol.MaximumMessageCharacters / 2;
    private static readonly byte[] _trainerCallMarker = "pbTrainerBattle("u8.ToArray();
    private readonly TrackerConnectionState _state;
    private readonly Lock _sync = new();
    private Task<TrainerLocationIndexAssignmentPayload?>? _preparation;
    private bool _accepted;
    private int _generation;
    private string? _peerSession;
    private string? _peerRoot;

    /// <summary>
    /// Initializes a connection-scoped index provider without accessing the game installation.
    /// </summary>
    /// <param name="state">The connection state identifying the current peer and installation.</param>
    internal TrackerTrainerLocationIndex(TrackerConnectionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <summary>
    /// Prepares a supported installation index outside the game thread and returns entries until the game acknowledges them.
    /// </summary>
    /// <param name="cancellationToken">The token cancelling this caller's wait without cancelling shared preparation.</param>
    /// <returns>The full index, or null after acknowledgment or when native Ruby lookup is required.</returns>
    /// <exception cref="IOException">Thrown when the peer disconnects while its index is being prepared.</exception>
    internal async Task<TrainerLocationIndexAssignmentPayload?> GetAssignmentAsync(CancellationToken cancellationToken)
    {
        Task<TrainerLocationIndexAssignmentPayload?> preparation;
        int generation;
        lock (_sync)
        {
            GameHandshakePayload? game = _state.Snapshot.Game;
            if (!OperatingSystem.IsWindows() || game?.TrainerLocationIndexVersion != TrainerLocationIndexAssignmentPayload.CurrentVersion || string.IsNullOrEmpty(game.SessionId))
                return null;

            if (_peerSession != game.SessionId || _peerRoot != game.GameRoot)
            {
                _generation++;
                _preparation = null;
                _accepted = false;
                _peerSession = game.SessionId;
                _peerRoot = game.GameRoot;
            }

            generation = _generation;
            _preparation ??= Task.Run(() => Build(game.GameRoot));
            preparation = _preparation;
        }

        TrainerLocationIndexAssignmentPayload? result = await preparation.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync)
        {
            GameHandshakePayload? game = _state.Snapshot.Game;
            if (generation != _generation || game?.SessionId != _peerSession || game?.GameRoot != _peerRoot || game?.TrainerLocationIndexVersion != TrainerLocationIndexAssignmentPayload.CurrentVersion)
                throw new IOException("The game disconnected during trainer location preparation.");

            return _accepted ? null : result;
        }
    }

    /// <summary>
    /// Checks the complete candidate request against the existing message budget before attaching optional index data.
    /// </summary>
    /// <typeparam name="TRequest">The occurrence request payload type.</typeparam>
    /// <param name="request">The request including its proposed index assignment.</param>
    /// <returns>Whether the serialized request leaves sufficient space for the correlated protocol envelope.</returns>
    internal static bool FitsRequest<TRequest>(TRequest request)
        => JsonSerializer.SerializeToUtf8Bytes(request, TrackerJson.Options).Length <= TrackerProtocol.MaximumMessageCharacters - RequestEnvelopeAllowance;

    /// <summary>
    /// Records whether the game accepted a supplied index so later requests carry no additional index data.
    /// </summary>
    /// <param name="contextToken">The accepted context, or null when original game indexing was used.</param>
    internal void Acknowledge(string? contextToken)
    {
        lock (_sync)
            _accepted = _preparation is { IsCompletedSuccessfully: true } && _preparation.Result?.ContextToken == contextToken && contextToken is not null;
    }

    /// <summary>
    /// Invalidates a disconnected peer's preparation and prevents it from supplying entries to a later connection.
    /// </summary>
    internal void Disconnect()
    {
        lock (_sync)
        {
            _generation++;
            _preparation = null;
            _accepted = false;
            _peerSession = null;
            _peerRoot = null;
        }
    }

    /// <summary>
    /// Reads exactly the authored map-file set used by Ruby and groups distinct map identifiers in filename order.
    /// </summary>
    /// <param name="gameRoot">The local installation supplied by the connected game.</param>
    /// <returns>A supported immutable assignment, or null for unreadable or unsupported source data.</returns>
    private static TrainerLocationIndexAssignmentPayload? Build(string gameRoot)
    {
        try
        {
            string root = Path.GetFullPath(gameRoot);
            Dictionary<string, List<int>> entries = new(StringComparer.Ordinal);
            int indexBytes = Encoding.UTF8.GetByteCount(root) * 6 + 256;
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, DataDirectory), MapPattern).Order(StringComparer.Ordinal))
            {
                string fileName = Path.GetFileName(path);
                if (fileName.Length != 13 || !int.TryParse(fileName.AsSpan(3, 3), NumberStyles.None, CultureInfo.InvariantCulture, out int mapId) || mapId < 1)
                    return null;

                if (new FileInfo(path).Length > MaximumMapFileBytes)
                    return null;

                List<string>? keys = ParseCalls(File.ReadAllBytes(path));
                if (keys is null)
                    return null;

                foreach (string key in keys)
                {
                    if (!entries.TryGetValue(key, out List<int>? maps))
                    {
                        entries[key] = maps = [];
                        indexBytes += ((key.Length + 2) / 3) * 4 + 16;
                    }

                    if (entries.Count > 10_000)
                        return null;

                    if (!maps.Contains(mapId))
                    {
                        maps.Add(mapId);
                        indexBytes += 12;
                    }

                    if (indexBytes > MaximumIndexBytes)
                        return null;
                }
            }

            return new TrainerLocationIndexAssignmentPayload
            {
                GameRoot = root, ContextToken = Guid.NewGuid().ToString(TokenFormat),
                Locations = entries.ToDictionary(entry => Convert.ToBase64String(Encoding.ASCII.GetBytes(entry.Key)), entry => (IReadOnlyList<int>)[.. entry.Value], StringComparer.Ordinal)
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reproduces the byte-level trainer-call expression without allocating a character copy of each map file.
    /// </summary>
    /// <param name="bytes">The complete authored map bytes, including script fragments.</param>
    /// <returns>Trainer keys in match order, or null when a matched key needs the original Ruby encoding behavior.</returns>
    private static List<string>? ParseCalls(byte[] bytes)
    {
        List<string> keys = [];
        ReadOnlySpan<byte> contents = bytes;
        ReadOnlySpan<byte> marker = _trainerCallMarker;
        int cursor = 0;
        while (cursor < contents.Length)
        {
            int found = contents[cursor..].IndexOf(marker);
            if (found < 0)
                break;

            int position = cursor + found + marker.Length;
            cursor = position;
            while (position < contents.Length && IsWhitespace(contents[position]))
                position++;

            if (position >= contents.Length || contents[position++] != (byte)':')
                continue;

            int typeStart = position;
            while (position < contents.Length && IsTrainerTypeCharacter(contents[position]))
                position++;

            if (position == typeStart)
                continue;

            int typeEnd = position;
            while (position < contents.Length && IsWhitespace(contents[position]))
                position++;

            if (position >= contents.Length || contents[position++] != (byte)',')
                continue;

            while (position < contents.Length && IsWhitespace(contents[position]))
                position++;

            if (position >= contents.Length || contents[position] != (byte)'"' && contents[position] != (byte)'\'')
                continue;

            int nameStart = ++position;
            int ending = contents[position..].IndexOfAny((byte)'"', (byte)'\'');
            if (ending <= 0)
                continue;

            position += ending;
            ReadOnlySpan<byte> name = contents[nameStart..position];
            if (name.Length + typeEnd - typeStart + 1 > 4096 || name.ContainsAnyInRange((byte)128, byte.MaxValue) || name.Contains((byte)0))
                return null;

            keys.Add(Encoding.ASCII.GetString(contents[typeStart..typeEnd]) + '\0' + Encoding.ASCII.GetString(name));
            if (keys.Count > 10_000)
                return null;

            cursor = position + 1;
        }

        return keys;
    }

    /// <summary>
    /// Matches the six whitespace bytes recognized by the original binary Ruby expression.
    /// </summary>
    /// <param name="value">The authored byte to examine.</param>
    /// <returns>Whether the byte is ASCII whitespace.</returns>
    private static bool IsWhitespace(byte value)
        => value is (byte)' ' or (byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or (byte)'\v';

    /// <summary>
    /// Matches the authored trainer-type symbol character class.
    /// </summary>
    /// <param name="value">The authored byte to examine.</param>
    /// <returns>Whether the byte is an ASCII letter, digit, or underscore.</returns>
    private static bool IsTrainerTypeCharacter(byte value)
        => value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or >= (byte)'0' and <= (byte)'9' or (byte)'_';
}
