using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// A write-only zlib datastream (RFC 1950): a raw <see cref="DeflateStream"/> between a header and an Adler-32 trailer
/// written here. The checksum is computed with the vectorized <see cref="Adler32"/>: the zlib of .NET computes it with
/// scalar code on Arm64. The bytes are those of a <see cref="ZLibStream"/> of the same level.
/// </summary>
internal sealed class PngZlibStream : Stream
{
    private readonly HeaderStream _output;
    private DeflateStream? _deflate;
    private uint _adler = Adler32.Compute([]);

    /// <param name="stream">The stream receiving the compressed data.</param>
    /// <param name="level">The zlib level (0 to 9).</param>
    /// <param name="leaveOpen"><see langword="true"/> to leave <paramref name="stream"/> open when this stream is disposed.</param>
    public PngZlibStream(Stream stream, int level, bool leaveOpen)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, 9);
        _output = new HeaderStream(stream, GetHeaderFlags(level), leaveOpen);
        _deflate = new DeflateStream(_output, new ZLibCompressionOptions { CompressionLevel = level }, leaveOpen: true);
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => _deflate is not null;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    /// <summary>
    /// Gets the second byte of the zlib header that zlib-ng writes for a level with a 32 KiB window: its compression level
    /// field (0 below level 2, 1 below level 6, 2 at level 6, 3 above) and the check bits that make the header a multiple of 31.
    /// </summary>
    internal static byte GetHeaderFlags(int level)
    {
        var levelFlags = level switch
        {
            < 2 => 0,
            < 6 => 1,
            6 => 2,
            _ => 3,
        };

        var header = (0x78 << 8) | (levelFlags << 6);
        header += 31 - (header % 31);
        return (byte)header;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var deflate = _deflate;
        ObjectDisposedException.ThrowIf(deflate is null, this);
        _adler = Adler32.Append(_adler, buffer);
        deflate.Write(buffer);
    }

    public override void WriteByte(byte value) => Write([value]);

    public override void Flush() => _deflate?.Flush();

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing && _deflate is not null)
            {
                // The last deflate block, then the checksum of the uncompressed data
                _deflate.Dispose();
                _deflate = null;
                if (_output.Started)
                {
                    Span<byte> trailer = stackalloc byte[4];
                    BinaryPrimitives.WriteUInt32BigEndian(trailer, _adler);
                    _output.Write(trailer);
                }

                _output.Dispose();
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// The destination of the deflate data: like zlib, the header is written before the first bytes, so that a stream that
    /// writes nothing has no header (and no trailer) either.
    /// </summary>
    private sealed class HeaderStream(Stream inner, byte flags, bool leaveOpen) : Stream
    {
        public bool Started { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (buffer.IsEmpty)
                return;

            if (!Started)
            {
                inner.Write([0x78, flags]);
                Started = true;
            }

            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
