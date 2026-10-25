using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Ironmon.Tracker.Collection.History;

/// <summary>
/// Protects bounded archive chunks with a tracker-owned key and authenticates their order and final end marker.
/// </summary>
internal sealed class HistoryArchiveCipher : Stream
{
    internal const string Extension = ".irmhistory";
    private const string _magic = "IRMHIST2";
    private const string _keyPurpose = "Ironmon.HistoryArchive.v2";
    private const int _chunkBytes = 262144;
    private const int _keyBytes = 32;
    private readonly FileStream _file;
    private readonly bool _writing;
    private readonly byte[] _header = new byte[32];
    private readonly byte[] _buffer = new byte[_chunkBytes];
    private readonly AesGcm _cipher;
    private int _position;
    private int _length;
    private uint _ordinal;
    private bool _finished;

    /// <summary>
    /// Opens a fresh encrypted archive or an existing authenticated reader.
    /// </summary>
    /// <param name="path">The archive path.</param>
    /// <param name="profileKey">The tracker-owned thirty-two-byte profile and epoch key.</param>
    /// <param name="writing">Whether to create a new file without overwriting one.</param>
    internal HistoryArchiveCipher(string path, ReadOnlySpan<byte> profileKey, bool writing)
    {
        if (profileKey.Length != _keyBytes)
            throw new ArgumentException("The tracker history key has an invalid size.", nameof(profileKey));

        _writing = writing;
        _file = new(path, writing ? FileMode.CreateNew : FileMode.Open, writing ? FileAccess.Write : FileAccess.Read, FileShare.None);
        try
        {
            if (writing)
            {
                Encoding.ASCII.GetBytes(_magic).CopyTo(_header, 0);
                RandomNumberGenerator.Fill(_header.AsSpan(8));
                _file.Write(_header);
            }
            else
            {
                _file.ReadExactly(_header);
                if (!_header.AsSpan(0, 8).SequenceEqual(Encoding.ASCII.GetBytes(_magic)))
                    throw new InvalidDataException("This is not a supported Ironmon history archive.");
            }

            Span<byte> key = stackalloc byte[_keyBytes];
            try
            {
                HKDF.DeriveKey(HashAlgorithmName.SHA256, profileKey, key, _header.AsSpan(8, 16), Encoding.UTF8.GetBytes(_keyPurpose));
                _cipher = new(key, 16);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets whether this stream decrypts input.
    /// </summary>
    public override bool CanRead => !_writing;
    /// <summary>
    /// Gets whether seeking is supported.
    /// </summary>
    public override bool CanSeek => false;
    /// <summary>
    /// Gets whether this stream encrypts output.
    /// </summary>
    public override bool CanWrite => _writing;
    /// <summary>
    /// Gets the unsupported plaintext length.
    /// </summary>
    public override long Length => throw new NotSupportedException();
    /// <summary>
    /// Gets or sets the unsupported plaintext position.
    /// </summary>
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>
    /// Reads authenticated plaintext without exposing an unverified chunk.
    /// </summary>
    /// <param name="buffer">Receives plaintext.</param>
    /// <param name="offset">The first destination byte.</param>
    /// <param name="count">The maximum requested bytes.</param>
    /// <returns>The verified plaintext bytes read, or zero at authenticated end.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_writing)
            throw new NotSupportedException();

        if (count == 0 || _finished)
            return 0;

        if (_position == _length)
            ReadFrame();

        if (_finished)
            return 0;

        int take = Math.Min(count, _length - _position);
        _buffer.AsSpan(_position, take).CopyTo(buffer.AsSpan(offset, take));
        _position += take;
        return take;
    }

    /// <summary>
    /// Buffers plaintext into fixed-size authenticated chunks.
    /// </summary>
    /// <param name="buffer">The compressed plaintext source.</param>
    /// <param name="offset">The first source byte.</param>
    /// <param name="count">The source bytes to consume.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        if (!_writing || _finished)
            throw new NotSupportedException();

        while (count > 0)
        {
            int take = Math.Min(count, _chunkBytes - _length);
            buffer.AsSpan(offset, take).CopyTo(_buffer.AsSpan(_length));
            _length += take;
            offset += take;
            count -= take;
            if (_length == _chunkBytes)
            {
                WriteFrame(_length);
                _length = 0;
            }
        }
    }

    /// <summary>
    /// Completes and durably flushes output, or verifies the reader's final marker.
    /// </summary>
    internal void Complete()
    {
        if (_writing && !_finished)
        {
            if (_length > 0)
                WriteFrame(_length);

            WriteFrame(0);
            _file.Flush(true);
            _finished = true;
        }
        else if (!_writing && (ReadByte() != -1 || !_finished || _file.Position != _file.Length))
        {
            throw new InvalidDataException("The archive has trailing or incomplete data.");
        }
    }

    /// <summary>
    /// Constructs a unique nonce and authenticates each chunk's header, ordinal and size.
    /// </summary>
    /// <param name="length">The bounded plaintext size.</param>
    /// <returns>The nonce and authenticated context for this frame.</returns>
    private (byte[] Nonce, byte[] Context) Context(int length)
    {
        if (_ordinal == uint.MaxValue)
            throw new InvalidDataException("The archive exceeds its supported size.");

        byte[] nonce = new byte[12];
        _header.AsSpan(24, 8).CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), _ordinal++);
        byte[] context = new byte[40];
        _header.CopyTo(context, 0);
        nonce.AsSpan(8).CopyTo(context.AsSpan(32));
        BinaryPrimitives.WriteInt32LittleEndian(context.AsSpan(36), length);
        return (nonce, context);
    }

    /// <summary>
    /// Encrypts one complete frame including the explicit empty end marker.
    /// </summary>
    /// <param name="length">The plaintext bytes to encrypt.</param>
    private void WriteFrame(int length)
    {
        var (nonce, context) = Context(length);
        byte[] encrypted = new byte[length];
        byte[] tag = new byte[16];
        _cipher.Encrypt(nonce, _buffer.AsSpan(0, length), encrypted, tag, context);
        _file.Write(context.AsSpan(36, 4));
        _file.Write(encrypted);
        _file.Write(tag);
    }

    /// <summary>
    /// Verifies a bounded encrypted frame before making its bytes readable.
    /// </summary>
    private void ReadFrame()
    {
        Span<byte> size = stackalloc byte[4];
        _file.ReadExactly(size);
        int length = BinaryPrimitives.ReadInt32LittleEndian(size);
        if (length < 0 || length > _chunkBytes)
            throw new InvalidDataException("An archive chunk has an invalid size.");

        byte[] encrypted = new byte[length];
        byte[] tag = new byte[16];
        _file.ReadExactly(encrypted);
        _file.ReadExactly(tag);
        var (nonce, context) = Context(length);
        _cipher.Decrypt(nonce, encrypted, tag, _buffer.AsSpan(0, length), context);
        _position = 0;
        _length = length;
        _finished = length == 0;
    }

    /// <summary>
    /// Flushes ciphertext already emitted by the compressor.
    /// </summary>
    public override void Flush()
        => _file.Flush();
    /// <summary>
    /// Rejects seeking in an authenticated sequential archive.
    /// </summary>
    /// <param name="offset">The unsupported offset.</param>
    /// <param name="origin">The unsupported origin.</param>
    /// <returns>No value; seeking always throws.</returns>
    public override long Seek(long offset, SeekOrigin origin)
        => throw new NotSupportedException();
    /// <summary>
    /// Rejects modifying an authenticated stream's length.
    /// </summary>
    /// <param name="value">The unsupported new length.</param>
    public override void SetLength(long value)
        => throw new NotSupportedException();

    /// <summary>
    /// Releases the cipher and erases the plaintext buffer without finalizing partial output.
    /// </summary>
    /// <param name="disposing">Whether managed resources should be released.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cipher.Dispose();
            _file.Dispose();
            CryptographicOperations.ZeroMemory(_buffer);
        }
        base.Dispose(disposing);
    }
}
