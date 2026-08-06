using System.Text;

namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Reads newline-delimited tracker messages from a persistent stream.
/// </summary>
public sealed class TrackerMessageReader : IDisposable
{
    private readonly StreamReader _reader;

    /// <summary>
    /// Initializes a message reader over a readable stream.
    /// </summary>
    /// <param name="stream">The stream from which messages are read.</param>
    /// <param name="leaveOpen">Whether disposing the reader leaves the stream open.</param>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the stream is not readable.</exception>
    public TrackerMessageReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The tracker message stream must be readable.", nameof(stream));

        UTF8Encoding encoding = new(false, true);
        _reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: leaveOpen);
    }

    /// <summary>
    /// Reads the next complete tracker message.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the read.</param>
    /// <returns>The next message, or null after a clean end of stream.</returns>
    /// <exception cref="TrackerProtocolException">
    /// Thrown when the message exceeds the framing limit or is invalid.
    /// </exception>
    public async ValueTask<TrackerMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        string? line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
            return null;

        if (line.Length > TrackerProtocol.MaximumMessageCharacters)
            throw new TrackerProtocolException("The tracker message exceeds the framing limit.");

        return TrackerMessageCodec.Deserialize(line);
    }

    /// <summary>
    /// Releases the underlying text reader and optionally its stream.
    /// </summary>
    public void Dispose() => _reader.Dispose();
}
