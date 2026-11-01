using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Numerics;
using Meziantou.Framework.Hashing;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Writes a PNG container: signature, IHDR, PLTE, IDAT and IEND, with the CRC32 each chunk needs.
/// </summary>
internal static class PngWriter
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly byte[] IhdrChunkType = [73, 72, 68, 82];
    private static readonly byte[] PlteChunkType = [80, 76, 84, 69];
    private static readonly byte[] TrnsChunkType = [116, 82, 78, 83];
    private static readonly byte[] IdatChunkType = [73, 68, 65, 84];
    private static readonly byte[] IendChunkType = [73, 69, 78, 68];

    /// <summary>
    /// The largest IDAT chunk written. libpng uses the same size: the 12 bytes of chunk overhead
    /// are negligible, and the buffer is small enough to rent for every image.
    /// </summary>
    internal const int DefaultMaxIdatChunkLength = 8192;

    /// <summary>
    /// Writes a two-colour indexed PNG at one bit per pixel.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="width">The image width in pixels.</param>
    /// <param name="height">The image height in pixels.</param>
    /// <param name="lightColor">The colour for a clear bit.</param>
    /// <param name="darkColor">The colour for a set bit.</param>
    /// <param name="renderRow">
    /// Fills one packed scanline and returns how many consecutive rows share it. The span is
    /// cleared before each call and holds one bit per pixel, most significant bit leftmost; set a
    /// bit to make that pixel dark.
    /// </param>
    /// <param name="maxIdatChunkLength">The largest IDAT chunk to write, in bytes.</param>
    /// <remarks>
    /// <para>
    /// QR codes and barcodes are two-colour images, so an indexed palette stores them at a
    /// thirty-second of the bytes truecolour-with-alpha needs. A scanline is rendered once and
    /// reused for the rows that repeat it, which is every row of a module but the first.
    /// </para>
    /// <para>
    /// The headers are written first. Rows are then compressed as they are produced into one zlib
    /// stream, which is split into IDAT chunks of at most <paramref name="maxIdatChunkLength"/>
    /// bytes as it fills them. Peak memory is three scanlines, one chunk and the compressor's
    /// state, whatever the image height, and the destination stream needs neither seeking nor
    /// reading. If rendering or writing fails, the destination holds an incomplete PNG.
    /// </para>
    /// </remarks>
    public static void WritePalette(Stream stream, int width, int height, Color lightColor, Color darkColor, PngRowRenderer renderRow, int maxIdatChunkLength = DefaultMaxIdatChunkLength)
    {
        var bytesPerRow = GetBytesPerRow(width);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxIdatChunkLength, 1);

        stream.Write(Signature);

        Span<byte> ihdrData = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdrData[4..], (uint)height);
        ihdrData[8] = 1;  // Bit depth
        ihdrData[9] = 3;  // Colour type: indexed
        ihdrData[10] = 0; // Compression method
        ihdrData[11] = 0; // Filter method
        ihdrData[12] = 0; // Interlace method
        WriteChunk(stream, IhdrChunkType, ihdrData);

        Span<byte> palette = [lightColor.Red, lightColor.Green, lightColor.Blue, darkColor.Red, darkColor.Green, darkColor.Blue];
        WriteChunk(stream, PlteChunkType, palette);

        if (lightColor.Alpha is not byte.MaxValue || darkColor.Alpha is not byte.MaxValue)
        {
            Span<byte> transparency = [lightColor.Alpha, darkColor.Alpha];
            WriteChunk(stream, TrnsChunkType, transparency);
        }

        var chunkBuffer = ArrayPool<byte>.Shared.Rent(maxIdatChunkLength);
        try
        {
            var idatStream = new IdatChunkStream(stream, chunkBuffer.AsMemory(0, maxIdatChunkLength));
            using (var compressionStream = new ZLibStream(idatStream, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                CompressRows(compressionStream, bytesPerRow, height, renderRow);
            }

            idatStream.WriteLastChunk();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunkBuffer);
        }

        WriteChunk(stream, IendChunkType, ReadOnlySpan<byte>.Empty);
    }

    /// <summary>
    /// Gets the number of bytes of a packed scanline at one bit per pixel, without its filter byte.
    /// </summary>
    /// <param name="width">The image width in pixels.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not positive.</exception>
    internal static int GetBytesPerRow(int width)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);

        // (width + 7) / 8 overflows for the widest images, so round up without the addition. The
        // result is at most 2^28, so the scanline and its filter byte always fit in an array.
        return (width >> 3) + ((width & 7) is 0 ? 0 : 1);
    }

    /// <summary>
    /// Sets the bits of the pixels in [<paramref name="start"/>, <paramref name="start"/> + <paramref name="count"/>) of a packed scanline.
    /// </summary>
    internal static void SetPixels(Span<byte> row, int start, int count)
    {
        if (count <= 0)
            return;

        var last = start + count - 1;
        var firstByte = start >> 3;
        var lastByte = last >> 3;
        var firstMask = (byte)(0xFF >> (start & 7));
        var lastMask = (byte)(0xFF << (7 - (last & 7)));
        if (firstByte == lastByte)
        {
            row[firstByte] |= (byte)(firstMask & lastMask);
            return;
        }

        row[firstByte] |= firstMask;
        row[(firstByte + 1)..lastByte].Fill(0xFF);
        row[lastByte] |= lastMask;
    }

    private static void CompressRows(ZLibStream compressionStream, int bytesPerRow, int height, PngRowRenderer renderRow)
    {
        var current = new byte[bytesPerRow];
        var previous = new byte[bytesPerRow];
        var filtered = new byte[bytesPerRow + 1];

        var row = 0;
        while (row < height)
        {
            Array.Clear(current);
            var rowCount = renderRow(current, row);
            if (rowCount < 1 || rowCount > height - row)
                throw new InvalidOperationException($"The row renderer returned {rowCount} rows at row {row} of {height}.");

            // The Up filter turns a row identical to the one above it into zeroes, which is
            // the common case here because every module spans ModuleSize rows.
            if (row is 0)
            {
                filtered[0] = 0; // None
                current.CopyTo(filtered.AsSpan(1));
            }
            else
            {
                filtered[0] = 2; // Up
                Subtract(current, previous, filtered.AsSpan(1));
            }

            compressionStream.Write(filtered);
            (previous, current) = (current, previous);

            if (rowCount > 1)
            {
                // The repeated rows are identical to the one above them, so their Up filter is
                // all zeroes: write it without rendering or subtracting anything.
                filtered[0] = 2; // Up
                filtered.AsSpan(1).Clear();
                for (var i = 1; i < rowCount; i++)
                {
                    compressionStream.Write(filtered);
                }
            }

            row += rowCount;
        }
    }

    private static void Subtract(ReadOnlySpan<byte> current, ReadOnlySpan<byte> previous, Span<byte> destination)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            for (; i <= current.Length - Vector<byte>.Count; i += Vector<byte>.Count)
            {
                (new Vector<byte>(current[i..]) - new Vector<byte>(previous[i..])).CopyTo(destination[i..]);
            }
        }

        for (; i < current.Length; i++)
        {
            destination[i] = (byte)(current[i] - previous[i]);
        }
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> data)
    {
        Span<byte> uintBuffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, (uint)data.Length);
        stream.Write(uintBuffer);
        stream.Write(chunkType);
        stream.Write(data);

        var crc = Crc32.Append(Crc32.Compute(chunkType), data);
        BinaryPrimitives.WriteUInt32BigEndian(uintBuffer, crc);
        stream.Write(uintBuffer);
    }

    /// <summary>
    /// A write-only stream that splits what is written to it into IDAT chunks of a bounded length.
    /// </summary>
    private sealed class IdatChunkStream : Stream
    {
        [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The caller owns the destination stream")]
        private readonly Stream _destination;
        private readonly Memory<byte> _chunk;
        private int _length;

        public IdatChunkStream(Stream destination, Memory<byte> chunk)
        {
            _destination = destination;
            _chunk = chunk;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                var count = Math.Min(buffer.Length, _chunk.Length - _length);
                buffer[..count].CopyTo(_chunk.Span[_length..]);
                _length += count;
                buffer = buffer[count..];

                if (_length == _chunk.Length)
                {
                    WriteChunk(_destination, IdatChunkType, _chunk.Span);
                    _length = 0;
                }
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void WriteByte(byte value)
        {
            Write([value]);
        }

        /// <summary>Writes the data left in the buffer, if any, as the last IDAT chunk.</summary>
        public void WriteLastChunk()
        {
            if (_length > 0)
            {
                WriteChunk(_destination, IdatChunkType, _chunk.Span[.._length]);
                _length = 0;
            }
        }

        // Chunks are only written when they are full, or at the end: flushing does not cut one short.
        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
