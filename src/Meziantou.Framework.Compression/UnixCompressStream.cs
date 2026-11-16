using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Compression.Internals.Lzw;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Decompresses the data of the Unix <c>compress</c> command (.Z): LZW codes of 9 to 16 bits, with or without the block
/// mode that clears the table. Invalid data throw an <see cref="InvalidDataException"/>. This stream cannot compress data.
/// </summary>
public sealed class UnixCompressStream : Stream
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The decoder disposes it")]
    private readonly Stream _stream;
    private readonly UnixCompressDecoderStream _decoder;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="UnixCompressStream"/> class to decompress a stream.</summary>
    /// <param name="stream">The stream to read the compressed data from.</param>
    /// <param name="leaveOpen">Whether <paramref name="stream"/> stays open when this stream is disposed.</param>
    public UnixCompressStream(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream is not readable.", nameof(stream));

        _stream = stream;
        _decoder = new UnixCompressDecoderStream(stream, leaveOpen);
    }

    /// <summary>Gets the stream the compressed data are read from.</summary>
    public Stream BaseStream => _stream;

    /// <inheritdoc />
    public override bool CanRead => !_disposed;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _decoder.Read(buffer);
    }

    /// <inheritdoc />
    public override int ReadByte()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _decoder.ReadByte();
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _decoder.ReadAsync(buffer, cancellationToken);
    }

    /// <summary>Throws a <see cref="NotSupportedException"/>: this stream cannot compress data.</summary>
    /// <param name="buffer">The data.</param>
    /// <param name="offset">The offset of the data in <paramref name="buffer"/>.</param>
    /// <param name="count">The number of bytes.</param>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("The compress format can only be decompressed.");

    /// <summary>Throws a <see cref="NotSupportedException"/>: this stream cannot compress data.</summary>
    /// <param name="buffer">The data.</param>
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException("The compress format can only be decompressed.");

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _decoder.Dispose();
        }

        base.Dispose(disposing);
    }
}
