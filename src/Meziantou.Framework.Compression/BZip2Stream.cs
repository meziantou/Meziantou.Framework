using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.BZip2;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Compresses or decompresses bzip2 data (.bz2), as <see cref="GZipStream"/> does for gzip. The decompression reads the
/// concatenated streams of pbzip2 and lbzip2 and the randomized blocks of old versions of bzip2, checks the CRC of each
/// block and of each stream, and throws an <see cref="InvalidDataException"/> for invalid data. The compression produces
/// the data of <c>bzip2</c>, which ends when the stream is disposed.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Compression.Tests/BZip2StreamTests.cs" ref:region="BZip2Stream_RoundTrip" ref:dedent="true">
/// var data = "Hello, bzip2! Hello, bzip2!"u8.ToArray();
///
/// using var compressed = new MemoryStream();
/// using (var bzip2 = new BZip2Stream(compressed, new BZip2CompressionOptions { BlockSize = 9 }, leaveOpen: true))
/// {
///     bzip2.Write(data);
/// }
///
/// compressed.Position = 0;
/// using var decompress = new BZip2Stream(compressed, CompressionMode.Decompress);
/// using var result = new MemoryStream();
/// decompress.CopyTo(result);
/// Assert.Equal(data, result.ToArray());
/// </code>
/// </example>
public sealed class BZip2Stream : Stream
{
    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The inner stream disposes it")]
    private readonly Stream _stream;
    private readonly Stream _inner;
    private readonly CompressionMode _mode;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="BZip2Stream"/> class to compress or decompress a stream.</summary>
    /// <param name="stream">The stream to read the compressed data from, or to write them to.</param>
    /// <param name="mode">Whether to compress (with the blocks of 900 kB of <c>bzip2 -9</c>) or to decompress.</param>
    /// <param name="leaveOpen">Whether <paramref name="stream"/> stays open when this stream is disposed.</param>
    public BZip2Stream(Stream stream, CompressionMode mode, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);

        _stream = stream;
        _mode = mode;
        switch (mode)
        {
            case CompressionMode.Decompress:
                if (!stream.CanRead)
                    throw new ArgumentException("The stream is not readable.", nameof(stream));

                _inner = new BZip2DecoderStream(stream, leaveOpen);
                break;

            case CompressionMode.Compress:
                if (!stream.CanWrite)
                    throw new ArgumentException("The stream is not writable.", nameof(stream));

                _inner = new BZip2EncoderStream(stream, 9, leaveOpen);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>Initializes a new instance of the <see cref="BZip2Stream"/> class to compress a stream at a compression level.</summary>
    /// <param name="stream">The stream to write the compressed data to.</param>
    /// <param name="compressionLevel">
    /// The compression level: <see cref="CompressionLevel.Fastest"/> and <see cref="CompressionLevel.NoCompression"/> use blocks of
    /// 100 kB, the other levels use blocks of 900 kB. The data are always compressed.
    /// </param>
    /// <param name="leaveOpen">Whether <paramref name="stream"/> stays open when this stream is disposed.</param>
    public BZip2Stream(Stream stream, CompressionLevel compressionLevel, bool leaveOpen = false)
        : this(stream, new BZip2CompressionOptions { BlockSize = compressionLevel is CompressionLevel.Fastest or CompressionLevel.NoCompression ? 1 : 9 }, leaveOpen)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="BZip2Stream"/> class to compress a stream with options.</summary>
    /// <param name="stream">The stream to write the compressed data to.</param>
    /// <param name="options">The options of the compression.</param>
    /// <param name="leaveOpen">Whether <paramref name="stream"/> stays open when this stream is disposed.</param>
    public BZip2Stream(Stream stream, BZip2CompressionOptions options, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream is not writable.", nameof(stream));

        _stream = stream;
        _mode = CompressionMode.Compress;
        _inner = new BZip2EncoderStream(stream, options.BlockSize, leaveOpen);
    }

    /// <summary>Gets the stream the compressed data are read from or written to.</summary>
    public Stream BaseStream => _stream;

    /// <inheritdoc />
    public override bool CanRead => !_disposed && _mode is CompressionMode.Decompress;

    /// <inheritdoc />
    public override bool CanWrite => !_disposed && _mode is CompressionMode.Compress;

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
        EnsureMode(CompressionMode.Decompress);
        return _inner.Read(buffer);
    }

    /// <inheritdoc />
    public override int ReadByte()
    {
        EnsureMode(CompressionMode.Decompress);
        return _inner.ReadByte();
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
        EnsureMode(CompressionMode.Decompress);
        return _inner.ReadAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        EnsureMode(CompressionMode.Compress);
        _inner.Write(buffer);
    }

    /// <inheritdoc />
    public override void WriteByte(byte value)
    {
        EnsureMode(CompressionMode.Compress);
        _inner.WriteByte(value);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        EnsureMode(CompressionMode.Compress);
        return _inner.WriteAsync(buffer, cancellationToken);
    }

    /// <summary>Does nothing: a bzip2 block is written when it is full, or when the stream is disposed.</summary>
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
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
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureMode(CompressionMode mode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_mode != mode)
            throw new NotSupportedException(mode is CompressionMode.Decompress ? "The stream compresses data: it cannot be read." : "The stream decompresses data: it cannot be written.");
    }
}
