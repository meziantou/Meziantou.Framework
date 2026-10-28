using System.Buffers.Binary;
using System.IO.Compression;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The zlib datastreams (RFC 1950) of the ZIPS, ZIP and PXR24 compressions (compression notes, sections 4 and 6): one
/// stream per chunk, which must produce exactly the expected bytes and occupy the whole stored data.
/// </summary>
internal static class ExrZlib
{
    /// <summary>Decompresses a chunk.</summary>
    /// <param name="compressed">The stored data of the chunk, copied to a buffer of the scope.</param>
    /// <param name="destination">Receives exactly its length of bytes.</param>
    /// <exception cref="InvalidImageContentException">The datastream is malformed, produces another number of bytes, or is followed by other bytes.</exception>
    public static void Inflate(PooledBuffer compressed, Span<byte> destination)
    {
        if (compressed.Length < 6)
            throw ExrFormat.Invalid("the zlib data of a chunk are truncated.");

        try
        {
            using var input = new MemoryStream(compressed.RawBuffer, 0, compressed.Length, writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            var written = 0;
            while (written < destination.Length)
            {
                var read = zlib.Read(destination[written..]);
                if (read <= 0)
                    throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the zlib data of a chunk produce {written} bytes; {destination.Length} are expected."));

                written += read;
            }

            Span<byte> extra = stackalloc byte[1];
            if (zlib.Read(extra) != 0)
                throw ExrFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"the zlib data of a chunk produce more than {destination.Length} bytes."));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidImageContentException("The OpenEXR file is invalid: the zlib data of a chunk are malformed.", ImageFormat.OpenExr, exception);
        }
        catch (IOException exception)
        {
            // The source is in memory: an IOException is a zlib error that is not a data error (a preset dictionary)
            throw new InvalidImageContentException("The OpenEXR file is invalid: the zlib data of a chunk are malformed.", ImageFormat.OpenExr, exception);
        }

        // ZLibStream reports a truncated stream as a clean end, and does not tell where the stream ends: the checksum
        // must be the last four bytes of the chunk
        var expected = BinaryPrimitives.ReadUInt32BigEndian(compressed.Span[^4..]);
        if (Adler32.Compute(destination) != expected)
            throw ExrFormat.Invalid("the zlib data of a chunk are truncated, followed by other bytes, or do not match their checksum.");
    }

    /// <summary>Compresses a chunk.</summary>
    /// <param name="source">The bytes to compress.</param>
    /// <param name="destination">Receives the datastream.</param>
    /// <returns>The length of the datastream, or -1 when it does not fit <paramref name="destination"/>.</returns>
    public static int Deflate(ReadOnlySpan<byte> source, PooledBuffer destination)
    {
        using var output = new BoundedStream(destination);
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(source);
        }

        return output.Overflowed ? -1 : output.Written;
    }

    /// <summary>A write-only stream over a buffer of fixed capacity that records an overflow instead of growing.</summary>
    private sealed class BoundedStream(PooledBuffer target) : Stream
    {
        public int Written { get; private set; }

        public bool Overflowed { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => Written;

        public override long Position
        {
            get => Written;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Overflowed)
                return;

            var span = target.Span;
            if (buffer.Length > span.Length - Written)
            {
                Overflowed = true;
                return;
            }

            buffer.CopyTo(span[Written..]);
            Written += buffer.Length;
        }
    }
}
