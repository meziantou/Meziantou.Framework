using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Compression;

/// <summary>
/// The compression context of a <c>CompressionStream</c> (https://compression.spec.whatwg.org/#compressionstream): the
/// chunks are written to a compression stream of the base class library, whose output is collected after each chunk.
/// </summary>
internal sealed class Compressor : IDisposable
{
    private readonly MemoryStream _output = new();
    private readonly CompressionFormat _format;
    private readonly Stream _stream;
    private bool _hasInput;
    private bool _finished;

    public Compressor(CompressionFormat format)
    {
        _format = format;
        _stream = format switch
        {
            CompressionFormat.Brotli => new BrotliStream(_output, CompressionLevel.Optimal, leaveOpen: true),
            CompressionFormat.Deflate => new ZLibStream(_output, CompressionLevel.Optimal, leaveOpen: true),
            CompressionFormat.DeflateRaw => new DeflateStream(_output, CompressionLevel.Optimal, leaveOpen: true),
            CompressionFormat.Gzip => new GZipStream(_output, CompressionLevel.Optimal, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    /// <summary>Compresses a chunk and returns the output available so far (possibly empty).</summary>
    public byte[] Compress(ReadOnlySpan<byte> input)
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _hasInput |= !input.IsEmpty;
        _stream.Write(input);
        return TakeOutput();
    }

    /// <summary>Compresses an empty input with the finish flag: returns the rest of the output.</summary>
    public byte[] Finish()
    {
        ObjectDisposedException.ThrowIf(_finished, this);
        _finished = true;
        _stream.Dispose();

        // The DEFLATE streams of the base class library write nothing when no data is written: the compressed data of an
        // empty input is a final block without any symbol
        if (!_hasInput && _output.Length == 0)
        {
            return _format switch
            {
                CompressionFormat.Deflate => [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01],
                CompressionFormat.DeflateRaw => [0x03, 0x00],
                CompressionFormat.Gzip => [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
                _ => [],
            };
        }

        return TakeOutput();
    }

    public void Dispose()
    {
        _finished = true;
        _stream.Dispose();
    }

    private byte[] TakeOutput()
    {
        if (_output.Length == 0)
            return [];

        var result = _output.ToArray();
        _output.SetLength(0);
        return result;
    }
}
