using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the pixels of one TIFF page into a <see cref="DecodedFrameSink"/>: strips or
/// tiles are read one block row at a time, un-predicted, expanded to the lossless working representation of the page and
/// written row by row, so the memory of a decode depends on the block geometry and never on the size of the page or of
/// the document.
/// </summary>
internal static class TiffPageDecoder
{
    /// <summary>Decodes a page into the sink as a single still frame.</summary>
    /// <param name="source">The input.</param>
    /// <param name="page">The resolved page.</param>
    /// <param name="sink">The destination, created with the page size and <see cref="TiffPage.SourcePixelFormat"/>.</param>
    /// <exception cref="InvalidImageContentException">A strip or tile is missing, malformed or outside the input.</exception>
    public static void Decode(RandomAccessSource source, TiffPage page, DecodedFrameSink sink)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(sink);

        var scope = source.Scope;
        var expandedLength = page.Size.Width * PixelFormats.GetBytesPerPixel(page.SourcePixelFormat);
        using var reader = new TiffBlockReader(source, page);
        using var expanded = scope.Rent(expandedLength, AllocationKind.DecoderState, clear: false);
        sink.BeginFrame(FrameDuration.Zero);
        if (page.IsTiled)
        {
            DecodeTiles(source, page, sink, reader, expanded.Span);
        }
        else
        {
            DecodeStrips(source, page, sink, reader, expanded.Span);
        }

        sink.EndImage();
    }

    private static void DecodeStrips(RandomAccessSource source, TiffPage page, DecodedFrameSink sink, TiffBlockReader reader, Span<byte> expanded)
    {
        using var strip = source.Scope.Rent(page.BlockRowLength * page.BlockHeight, AllocationKind.DecoderState, clear: false);
        using var lease = sink.LeaseCurrentFrame();
        for (var block = 0; block < page.BlocksDown; block++)
        {
            source.Context.CancellationToken.ThrowIfCancellationRequested();
            var firstRow = block * page.BlockHeight;
            var rowCount = Math.Min(page.BlockHeight, page.Size.Height - firstRow);
            var stored = strip.Span[..(rowCount * page.BlockRowLength)];
            reader.ReadBlock(block, rowCount, stored);
            for (var row = 0; row < rowCount; row++)
            {
                ExpandRow(page, stored.Slice(row * page.BlockRowLength, page.BlockRowLength), expanded);
                sink.WriteRow(lease, firstRow + row, expanded);
            }
        }
    }

    private static void DecodeTiles(RandomAccessSource source, TiffPage page, DecodedFrameSink sink, TiffBlockReader reader, Span<byte> expanded)
    {
        var packedPixelLength = page.SamplesPerPixel * page.BitsPerSample / 8;
        var bandRowLength = (int)page.RowLength;
        if (!CheckedSizes.TryMultiply(bandRowLength, page.BlockHeight, out var bandLength) || bandLength > CheckedSizes.MaxBufferLength)
            throw CheckedSizes.CreateOverflowException(source.Limits);

        using var tile = source.Scope.Rent(page.BlockRowLength * page.BlockHeight, AllocationKind.DecoderState, clear: false);
        using var band = source.Scope.Rent((int)bandLength, AllocationKind.DecoderState, clear: false);
        using var lease = sink.LeaseCurrentFrame();
        for (var blockRow = 0; blockRow < page.BlocksDown; blockRow++)
        {
            source.Context.CancellationToken.ThrowIfCancellationRequested();
            var firstRow = blockRow * page.BlockHeight;
            var rowCount = Math.Min(page.BlockHeight, page.Size.Height - firstRow);
            for (var blockColumn = 0; blockColumn < page.BlocksAcross; blockColumn++)
            {
                // A tile is always stored in full, even when it extends past the right or bottom edge of the page
                reader.ReadBlock((blockRow * page.BlocksAcross) + blockColumn, page.BlockHeight, tile.Span);
                var firstColumn = blockColumn * page.BlockWidth;
                var columnCount = Math.Min(page.BlockWidth, page.Size.Width - firstColumn);
                var copyLength = columnCount * packedPixelLength;
                for (var row = 0; row < rowCount; row++)
                {
                    tile.Span.Slice(row * page.BlockRowLength, copyLength)
                        .CopyTo(band.Span.Slice((row * bandRowLength) + (firstColumn * packedPixelLength), copyLength));
                }
            }

            for (var row = 0; row < rowCount; row++)
            {
                ExpandRow(page, band.Span.Slice(row * bandRowLength, bandRowLength), expanded);
                sink.WriteRow(lease, firstRow + row, expanded);
            }
        }
    }

    /// <summary>Expands one stored row to the lossless working representation of the page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="stored">The stored samples of the row; only the first <c>Width</c> pixels are read (a tile row is padded).</param>
    /// <param name="destination">Exactly <c>Width</c> pixels of <see cref="TiffPage.SourcePixelFormat"/>.</param>
    private static void ExpandRow(TiffPage page, ReadOnlySpan<byte> stored, Span<byte> destination)
    {
        var width = page.Size.Width;
        var invert = page.IsWhiteIsZero;
        if (page.BitsPerSample == 8)
        {
            switch (page.SamplesPerPixel)
            {
                case 1:
                    stored[..width].CopyTo(destination);
                    if (invert)
                    {
                        InvertBytes(destination[..width]);
                    }

                    break;

                case 2:
                    for (var x = 0; x < width; x++)
                    {
                        var gray = stored[x * 2];
                        if (invert)
                        {
                            gray = (byte)(255 - gray);
                        }

                        var offset = x * 4;
                        destination[offset] = gray;
                        destination[offset + 1] = gray;
                        destination[offset + 2] = gray;
                        destination[offset + 3] = stored[(x * 2) + 1];
                    }

                    break;

                default:
                    // RGB (3) and RGBA (4) samples already match Rgb24 and Rgba32
                    stored[..(width * page.SamplesPerPixel)].CopyTo(destination);
                    break;
            }

            return;
        }

        var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(destination));
        switch (page.SamplesPerPixel)
        {
            case 1:
                ReadSamples(page, stored, samples[..width]);
                if (invert)
                {
                    InvertSamples(samples[..width]);
                }

                break;

            case 2:
                for (var x = 0; x < width; x++)
                {
                    var gray = ReadSample(page, stored, x * 2);
                    if (invert)
                    {
                        gray = (ushort)(ushort.MaxValue - gray);
                    }

                    var offset = x * 4;
                    samples[offset] = gray;
                    samples[offset + 1] = gray;
                    samples[offset + 2] = gray;
                    samples[offset + 3] = ReadSample(page, stored, (x * 2) + 1);
                }

                break;

            case 3:
                // There is no 16-bit RGB pixel format: the samples are widened to Rgba64 with an opaque alpha channel
                for (var x = 0; x < width; x++)
                {
                    var offset = x * 4;
                    samples[offset] = ReadSample(page, stored, x * 3);
                    samples[offset + 1] = ReadSample(page, stored, (x * 3) + 1);
                    samples[offset + 2] = ReadSample(page, stored, (x * 3) + 2);
                    samples[offset + 3] = ushort.MaxValue;
                }

                break;

            default:
                ReadSamples(page, stored, samples[..(width * 4)]);
                break;
        }
    }

    private static void ReadSamples(TiffPage page, ReadOnlySpan<byte> stored, Span<ushort> destination)
    {
        var source = stored[..(destination.Length * 2)];
        if (page.IsBigEndian)
        {
            SampleEndianness.ReadBigEndian(source, destination);
        }
        else
        {
            SampleEndianness.ReadLittleEndian(source, destination);
        }
    }

    private static ushort ReadSample(TiffPage page, ReadOnlySpan<byte> stored, int index)
    {
        var offset = index * 2;
        return page.IsBigEndian
            ? (ushort)((stored[offset] << 8) | stored[offset + 1])
            : (ushort)((stored[offset + 1] << 8) | stored[offset]);
    }

    private static void InvertBytes(Span<byte> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (byte)(255 - values[i]);
        }
    }

    private static void InvertSamples(Span<ushort> values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (ushort)(ushort.MaxValue - values[i]);
        }
    }
}
