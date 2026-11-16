// The LZMA, LZMA2 and XZ codecs are ports of the LZMA SDK by Igor Pavlov and of xz-utils (liblzma), which are in the public domain.
using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression;

/// <summary>
/// Compresses and decompresses the "LZMA alone" format (.lzma) of the LZMA SDK and of xz-utils, like <see cref="GZipStream"/> does for gzip.
/// </summary>
/// <remarks>
/// <para>
/// The format is a 13-byte header (the LZMA properties, the dictionary size and the uncompressed size), then the LZMA data.
/// The decompression reads the data up to the uncompressed size of the header, or up to the end marker when the size is
/// unknown. Corrupt data throws an <see cref="InvalidDataException"/>.
/// </para>
/// <para>
/// The compression writes an unknown size and an end marker. LZMA cannot end a block in the middle of the data:
/// <see cref="Flush"/> writes the compressed data available so far, and the data can be decompressed once the stream is disposed.
/// </para>
/// </remarks>
public sealed class LzmaStream : Stream
{
    private readonly CodecStreamCore _core;

    /// <summary>Initializes a new instance of the <see cref="LzmaStream"/> class that compresses with the default options, or decompresses.</summary>
    /// <param name="stream">The stream to write the compressed data to, or to read it from.</param>
    /// <param name="mode">Whether to compress or decompress.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the stream is disposed.</param>
    public LzmaStream(Stream stream, CompressionMode mode, bool leaveOpen = false)
    {
        _core = CodecStreamCore.Create(stream, mode, leaveOpen, input => new LzmaAloneReader(input, leaveOpen: true), output => CreateWriter(output, new LzmaCompressionOptions()));
    }

    /// <summary>Initializes a decompression stream whose decoder allocates at most <paramref name="maxMemoryUsage"/> bytes for its window (the archive readers).</summary>
    internal LzmaStream(Stream stream, bool leaveOpen, long maxMemoryUsage)
    {
        _core = CodecStreamCore.CreateDecompressor(stream, leaveOpen, input => new LzmaAloneReader(input, leaveOpen: true, maxMemoryUsage));
    }

    /// <summary>Initializes a new instance of the <see cref="LzmaStream"/> class that compresses at a compression level.</summary>
    /// <param name="stream">The stream to write the compressed data to.</param>
    /// <param name="compressionLevel">The compression level: <see cref="CompressionLevel.Optimal"/> is the preset 6, <see cref="CompressionLevel.Fastest"/> the preset 1, <see cref="CompressionLevel.NoCompression"/> the preset 0, and <see cref="CompressionLevel.SmallestSize"/> the preset 9.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the stream is disposed.</param>
    public LzmaStream(Stream stream, CompressionLevel compressionLevel, bool leaveOpen = false)
    {
        var options = new LzmaCompressionOptions { Preset = CodecStreamCore.GetPreset(compressionLevel) };
        _core = CodecStreamCore.CreateCompressor(stream, leaveOpen, output => CreateWriter(output, options));
    }

    /// <summary>Initializes a new instance of the <see cref="LzmaStream"/> class that compresses with options.</summary>
    /// <param name="stream">The stream to write the compressed data to.</param>
    /// <param name="options">The options of the compression.</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when the stream is disposed.</param>
    public LzmaStream(Stream stream, LzmaCompressionOptions options, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        _core = CodecStreamCore.CreateCompressor(stream, leaveOpen, output => CreateWriter(output, options));
    }

    /// <summary>Gets the stream that contains the compressed data.</summary>
    public Stream BaseStream => _core.BaseStream;

    /// <inheritdoc />
    public override bool CanRead => _core.CanRead;

    /// <inheritdoc />
    public override bool CanWrite => _core.CanWrite;

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

    private static LzmaEncoderStream CreateWriter(Stream output, LzmaCompressionOptions options) => LzmaAloneFormat.CreateEncoder(output, options.CreateSettings());

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return _core.Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => _core.Read(buffer);

    /// <inheritdoc />
    public override int ReadByte()
    {
        Span<byte> value = stackalloc byte[1];
        return _core.Read(value) is 1 ? value[0] : -1;
    }

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return _core.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => _core.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        _core.Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => _core.Write(buffer);

    /// <inheritdoc />
    public override void WriteByte(byte value) => _core.Write([value]);

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ValidateBufferArguments(buffer, offset, count);
        return _core.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => _core.WriteAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override void Flush() => _core.Flush();

    /// <inheritdoc />
    public override Task FlushAsync(CancellationToken cancellationToken) => _core.FlushAsync(cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _core.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _core.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
