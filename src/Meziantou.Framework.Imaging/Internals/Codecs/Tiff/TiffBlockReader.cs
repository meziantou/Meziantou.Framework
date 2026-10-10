using System.IO.Compression;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reads one strip or tile of a TIFF page into a buffer of exactly the size the geometry requires, and undoes the
/// horizontal differencing predictor.
/// </summary>
/// <remarks>
/// The decompressed size of a block is always known before the block is read (the number of rows times the padded row
/// length), so a Deflate stream is inflated into a fixed buffer and can never expand without bound: a block that produces
/// fewer bytes is reported as truncated, and the bytes it produces beyond the buffer are not read. The compressed length
/// itself comes from <c>StripByteCounts</c>/<c>TileByteCounts</c> and is validated against the length of the input first.
/// </remarks>
internal sealed class TiffBlockReader : IDisposable
{
    private readonly RandomAccessSource _source;
    private readonly TiffPage _page;
    private PooledBuffer? _inflateScratch;

    public TiffBlockReader(RandomAccessSource source, TiffPage page)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        _source = source;
        _page = page;
    }

    /// <summary>Reads, decompresses and un-predicts one block.</summary>
    /// <param name="index">The index of the block in <see cref="TiffPage.BlockOffsets"/>.</param>
    /// <param name="rowCount">The number of stored rows of the block (a short last strip stores fewer rows; a tile never does).</param>
    /// <param name="destination">Exactly <c>rowCount * BlockRowLength</c> bytes, filled with the stored samples.</param>
    /// <exception cref="InvalidImageContentException">The block is outside the input, shorter than its geometry requires, or its compressed data is malformed.</exception>
    public void ReadBlock(int index, int rowCount, Span<byte> destination)
    {
        var offset = _page.BlockOffsets[index];
        var storedLength = _page.BlockByteCounts[index];
        if (offset > long.MaxValue || storedLength > long.MaxValue)
            throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {_page.Index} stores the block {index} at offset {offset} with {storedLength} bytes, outside the input."));

        if (_page.IsDeflate)
        {
            if (storedLength == 0)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {_page.Index} declares an empty compressed block {index}."));

            using var compressed = _source.ReadToBuffer((long)offset, (long)storedLength);
            Inflate(compressed, destination, index);
        }
        else
        {
            if ((long)storedLength < destination.Length)
                throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The TIFF page {_page.Index} declares {storedLength} bytes for the uncompressed block {index}; its geometry needs {destination.Length}."));

            _source.Read((long)offset, destination);
        }

        if (_page.Predictor == TiffFormat.PredictorHorizontalDifferencing)
        {
            UndoHorizontalDifferencing(destination, rowCount);
        }
    }

    public void Dispose()
    {
        _inflateScratch?.Dispose();
        _inflateScratch = null;
    }

    private void Inflate(PooledBuffer compressed, Span<byte> destination, int index)
    {
        using var input = new MemoryStream(compressed.RawBuffer, 0, compressed.Length, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        try
        {
            var buffer = _inflateScratch ??= _source.Scope.Rent(64 * 1024, AllocationKind.DecoderState, clear: false);
            var written = 0;
            while (written < destination.Length)
            {
                var chunk = Math.Min(buffer.Length, destination.Length - written);
                var read = zlib.Read(buffer.Span[..chunk]);
                if (read <= 0)
                    throw TiffFormat.Invalid(string.Create(CultureInfo.InvariantCulture, $"The Deflate data of the TIFF block {index} of page {_page.Index} produced {written} bytes; {destination.Length} are required."));

                buffer.Span[..read].CopyTo(destination[written..]);
                written += read;
            }
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The Deflate data of the TIFF block {index} of page {_page.Index} is malformed."), ImageFormat.Tiff, exception);
        }
    }

    /// <summary>Undoes the horizontal differencing predictor row by row, over the stored (padded) width of the block.</summary>
    private void UndoHorizontalDifferencing(Span<byte> block, int rowCount)
    {
        var rowLength = _page.BlockRowLength;
        var samplesPerPixel = _page.SamplesPerPixel;
        for (var row = 0; row < rowCount; row++)
        {
            var line = block.Slice(row * rowLength, rowLength);
            if (_page.BitsPerSample == 8)
            {
                for (var i = samplesPerPixel; i < line.Length; i++)
                {
                    line[i] += line[i - samplesPerPixel];
                }
            }
            else
            {
                // 16-bit samples are stored in the byte order of the file; the differences are between the 16-bit values
                var count = line.Length / 2;
                for (var i = samplesPerPixel; i < count; i++)
                {
                    var previous = ReadSample(line, i - samplesPerPixel);
                    WriteSample(line, i, (ushort)(ReadSample(line, i) + previous));
                }
            }
        }
    }

    private ushort ReadSample(ReadOnlySpan<byte> line, int index)
    {
        var offset = index * 2;
        return _page.IsBigEndian
            ? (ushort)((line[offset] << 8) | line[offset + 1])
            : (ushort)((line[offset + 1] << 8) | line[offset]);
    }

    private void WriteSample(Span<byte> line, int index, ushort value)
    {
        var offset = index * 2;
        if (_page.IsBigEndian)
        {
            line[offset] = (byte)(value >> 8);
            line[offset + 1] = (byte)value;
        }
        else
        {
            line[offset] = (byte)value;
            line[offset + 1] = (byte)(value >> 8);
        }
    }
}
