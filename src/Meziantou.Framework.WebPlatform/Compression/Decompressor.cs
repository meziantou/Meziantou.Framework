using System.Buffers;
using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Compression;

/// <summary>
/// The decompression context of a <c>DecompressionStream</c> (https://compression.spec.whatwg.org/#decompressionstream).
/// The input is decoded as it is received; an invalid input throws an <see cref="InvalidDataException"/>.
/// </summary>
internal abstract class Decompressor : IDisposable
{
    public static Decompressor Create(CompressionFormat format) => format switch
    {
        CompressionFormat.Brotli => new BrotliDecompressor(),
        CompressionFormat.Deflate => new Inflater(InflaterWrapper.ZLib),
        CompressionFormat.DeflateRaw => new Inflater(InflaterWrapper.None),
        CompressionFormat.Gzip => new Inflater(InflaterWrapper.Gzip),
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>Whether the end of the compressed input has been reached.</summary>
    public abstract bool IsEnded { get; }

    /// <summary>Whether the end of the compressed input has been reached before the last chunk was fully consumed.</summary>
    public bool HasTrailingInput { get; protected set; }

    /// <summary>Decompresses a chunk and returns the output available so far (possibly empty).</summary>
    public abstract byte[] Decompress(ReadOnlySpan<byte> input);

    /// <summary>Decompresses an empty input with the finish flag: throws when the end of the compressed input has not been reached.</summary>
    public byte[] Finish()
    {
        if (!IsEnded)
            throw new InvalidDataException("The compressed data is truncated");

        return [];
    }

    /// <summary>Releases the native state of the decoder.</summary>
    public abstract void Dispose();
}

/// <summary>A Brotli decoder (RFC 7932), using the decoder of the base class library.</summary>
internal sealed class BrotliDecompressor : Decompressor
{
    private BrotliDecoder _decoder;
    private bool _ended;
    private bool _disposed;

    public override bool IsEnded => _ended;

    public override byte[] Decompress(ReadOnlySpan<byte> input)
    {
        HasTrailingInput = false;
        if (_ended)
        {
            HasTrailingInput = !input.IsEmpty;
            return [];
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        var output = new ArrayBufferWriter<byte>();
        while (true)
        {
            var status = _decoder.Decompress(input, output.GetSpan(65536), out var consumed, out var written);
            output.Advance(written);
            input = input[consumed..];
            switch (status)
            {
                case OperationStatus.Done:
                    _ended = true;
                    HasTrailingInput = !input.IsEmpty;
                    Dispose();
                    return output.WrittenSpan.ToArray();

                case OperationStatus.NeedMoreData:
                    return output.WrittenSpan.ToArray();

                case OperationStatus.DestinationTooSmall:
                    continue;

                default:
                    Dispose();
                    throw new InvalidDataException("The Brotli compressed data is invalid");
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _decoder.Dispose();
        }
    }
}
