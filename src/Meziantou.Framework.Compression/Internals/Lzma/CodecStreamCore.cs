using System.IO.Compression;

namespace Meziantou.Framework.Compression.Internals.Lzma;

/// <summary>
/// The implementation shared by <see cref="XzStream"/>, <see cref="LzmaStream"/> and <see cref="LZipStream"/>: a
/// decompression stream reads from a decoder over the base stream, a compression stream writes to an encoder whose output
/// is copied to the base stream after each write.
/// </summary>
internal sealed class CodecStreamCore
{
    private readonly bool _leaveOpen;
    private readonly PrefetchingReadStream? _input;
    private readonly Stream? _decoder;
    private readonly PendingWriteStream? _output;
    private readonly Stream? _encoder;
    private bool _disposed;

    private CodecStreamCore(Stream stream, bool leaveOpen, PrefetchingReadStream? input, Stream? decoder, PendingWriteStream? output, Stream? encoder)
    {
        BaseStream = stream;
        _leaveOpen = leaveOpen;
        _input = input;
        _decoder = decoder;
        _output = output;
        _encoder = encoder;
    }

    public Stream BaseStream { get; }

    public bool CanRead => !_disposed && _decoder is not null;

    public bool CanWrite => !_disposed && _encoder is not null;

    public static CodecStreamCore CreateDecompressor(Stream stream, bool leaveOpen, Func<Stream, Stream> createDecoder)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("The stream must be readable.", nameof(stream));

        var input = new PrefetchingReadStream(stream);
        return new CodecStreamCore(stream, leaveOpen, input, createDecoder(input), output: null, encoder: null);
    }

    public static CodecStreamCore CreateCompressor(Stream stream, bool leaveOpen, Func<Stream, Stream> createEncoder)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("The stream must be writable.", nameof(stream));

        var output = new PendingWriteStream();
        var encoder = createEncoder(output);
        var core = new CodecStreamCore(stream, leaveOpen, input: null, decoder: null, output, encoder);
        output.CopyPendingTo(stream);
        return core;
    }

    public static CodecStreamCore Create(Stream stream, CompressionMode mode, bool leaveOpen, Func<Stream, Stream> createDecoder, Func<Stream, Stream> createEncoder)
    {
        return mode switch
        {
            CompressionMode.Decompress => CreateDecompressor(stream, leaveOpen, createDecoder),
            CompressionMode.Compress => CreateCompressor(stream, leaveOpen, createEncoder),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "The compression mode is not supported."),
        };
    }

    /// <summary>Gets the preset of a <see cref="CompressionLevel"/>.</summary>
    public static int GetPreset(CompressionLevel compressionLevel)
    {
        return compressionLevel switch
        {
            CompressionLevel.Optimal => 6,
            CompressionLevel.Fastest => 1,
            CompressionLevel.NoCompression => 0,
            CompressionLevel.SmallestSize => 9,
            _ => throw new ArgumentOutOfRangeException(nameof(compressionLevel), compressionLevel, "The compression level is not supported."),
        };
    }

    public int Read(Span<byte> buffer)
    {
        var decoder = EnsureDecoder();
        return decoder.Read(buffer);
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        var decoder = EnsureDecoder();
        if (buffer.IsEmpty)
            return 0;

        await _input!.PrefetchAsync(cancellationToken).ConfigureAwait(false);
        return decoder.Read(buffer.Span);
    }

    public void Write(ReadOnlySpan<byte> buffer)
    {
        var encoder = EnsureEncoder();
        encoder.Write(buffer);
        _output!.CopyPendingTo(BaseStream);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        var encoder = EnsureEncoder();
        cancellationToken.ThrowIfCancellationRequested();
        encoder.Write(buffer.Span);
        await _output!.CopyPendingToAsync(BaseStream, cancellationToken).ConfigureAwait(false);
    }

    public void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_encoder is not null)
        {
            _encoder.Flush();
            _output!.CopyPendingTo(BaseStream);
            BaseStream.Flush();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_encoder is not null)
        {
            _encoder.Flush();
            await _output!.CopyPendingToAsync(BaseStream, cancellationToken).ConfigureAwait(false);
            await BaseStream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_encoder is not null)
            {
                _encoder.Dispose();
                _output!.CopyPendingTo(BaseStream);
                BaseStream.Flush();
            }
        }
        finally
        {
            _decoder?.Dispose();
            _input?.Dispose();
            if (!_leaveOpen)
            {
                BaseStream.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            if (_encoder is not null)
            {
                _encoder.Dispose();
                await _output!.CopyPendingToAsync(BaseStream, CancellationToken.None).ConfigureAwait(false);
                await BaseStream.FlushAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _decoder?.Dispose();
            _input?.Dispose();
            if (!_leaveOpen)
            {
                await BaseStream.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private Stream EnsureDecoder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _decoder ?? throw new NotSupportedException("The stream is a compression stream: it does not support reading.");
    }

    private Stream EnsureEncoder()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _encoder ?? throw new NotSupportedException("The stream is a decompression stream: it does not support writing.");
    }
}
