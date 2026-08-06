using System.Text;

namespace Ironmon.Tracker.Protocol;

/// <summary>
/// Writes newline-delimited tracker messages to a persistent stream.
/// </summary>
public sealed class TrackerMessageWriter : IAsyncDisposable
{
    private readonly StreamWriter _writer;

    /// <summary>
    /// Initializes a message writer over a writable stream.
    /// </summary>
    /// <param name="stream">The stream to which messages are written.</param>
    /// <param name="leaveOpen">Whether disposing the writer leaves the stream open.</param>
    /// <exception cref="ArgumentNullException">Thrown when stream is null.</exception>
    /// <exception cref="ArgumentException">Thrown when the stream is not writable.</exception>
    public TrackerMessageWriter(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The tracker message stream must be writable.", nameof(stream));

        UTF8Encoding encoding = new(false, true);
        _writer = new StreamWriter(stream, encoding, leaveOpen: leaveOpen)
        {
            NewLine = "\n"
        };
    }

    /// <summary>
    /// Writes and flushes one complete tracker message.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">The token that cancels the write.</param>
    /// <returns>A task representing the asynchronous write.</returns>
    public async ValueTask WriteAsync(TrackerMessage message, CancellationToken cancellationToken = default)
    {
        string json = TrackerMessageCodec.Serialize(message);
        await _writer.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Releases the underlying text writer and optionally its stream.
    /// </summary>
    /// <returns>A task representing asynchronous disposal.</returns>
    public ValueTask DisposeAsync() => _writer.DisposeAsync();
}
