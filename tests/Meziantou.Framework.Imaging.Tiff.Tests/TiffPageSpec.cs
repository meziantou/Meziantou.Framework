using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>The description of one page assembled by <see cref="TiffFileBuilder"/>.</summary>
internal sealed class TiffPageSpec
{
    public required int Width { get; init; }

    public required int Height { get; init; }

    public int BitsPerSample { get; init; } = 8;

    public int SamplesPerPixel { get; init; } = 1;

    /// <summary>0 (WhiteIsZero), 1 (BlackIsZero), 2 (RGB), or any other value to exercise the rejections.</summary>
    public int Photometric { get; init; } = 1;

    /// <summary>
    /// 1 (none), 5 (LZW), 8 (Adobe Deflate), 32773 (PackBits) and 32946 (Deflate) are applied to the samples by the
    /// builder; any other value is only declared (with <see cref="EncodedBlocks"/>, or to exercise the rejections).
    /// </summary>
    public int Compression { get; init; } = 1;

    /// <summary>The strips or tiles as they are stored, for the compressions the builder does not apply itself; <see cref="Samples"/> is then ignored.</summary>
    public byte[][]? EncodedBlocks { get; init; }

    public int? FillOrder { get; init; }

    public bool OmitBitsPerSample { get; init; }

    public int RowsPerStrip { get; init; } = int.MaxValue;

    public int? TileWidth { get; init; }

    public int? TileLength { get; init; }

    public int? Predictor { get; init; }

    public int? PlanarConfiguration { get; init; }

    public int? Orientation { get; init; }

    public int? SampleFormat { get; init; }

    public int[]? ExtraSamples { get; init; }

    public TiffResolutionSpec? Resolution { get; init; }

    public bool OmitByteCounts { get; init; }

    public bool OmitPhotometric { get; init; }

    public IList<TiffFieldSpec> ExtraFields { get; } = [];

    /// <summary>The samples of the page, row by row, packed and in the byte order of the file.</summary>
    public required byte[] Samples { get; init; }

    public bool IsTiled => TileWidth is not null;

    /// <summary>Splits the samples into the strips or tiles the directory will describe, compressing them when asked.</summary>
    /// <param name="bigEndian">Whether the samples are big-endian, which the predictors need to know.</param>
    public byte[][] BuildBlocks(bool bigEndian = false)
    {
        if (EncodedBlocks is not null)
            return EncodedBlocks;

        if (PlanarConfiguration == 2 && SamplesPerPixel > 1)
            return BuildPlanes(bigEndian);

        // Rows are padded to whole bytes; tile widths are multiples of 16 pixels, so a tile starts on a byte of the row
        var bitsPerPixel = SamplesPerPixel * BitsPerSample;
        var rowLength = ((Width * bitsPerPixel) + 7) / 8;
        var blocks = new List<byte[]>();
        if (IsTiled)
        {
            var tileWidth = TileWidth!.Value;
            var tileLength = TileLength!.Value;
            var tileRowLength = ((tileWidth * bitsPerPixel) + 7) / 8;
            for (var top = 0; top < Height; top += tileLength)
            {
                for (var left = 0; left < tileWidth * ((Width + tileWidth - 1) / tileWidth); left += tileWidth)
                {
                    // Edge tiles are stored in full; the padding is whatever the file says (zeros here)
                    var tile = new byte[tileRowLength * tileLength];
                    for (var y = 0; y < tileLength && top + y < Height; y++)
                    {
                        var columns = Math.Min(tileWidth, Width - left);
                        if (columns <= 0)
                            continue;

                        Samples.AsSpan(((top + y) * rowLength) + (left * bitsPerPixel / 8), ((columns * bitsPerPixel) + 7) / 8)
                            .CopyTo(tile.AsSpan(y * tileRowLength));
                    }

                    blocks.Add(Finish(tile, tileRowLength, bigEndian));
                }
            }

            return [.. blocks];
        }

        var rows = Math.Min(RowsPerStrip, Height);
        for (var top = 0; top < Height; top += rows)
        {
            var count = Math.Min(rows, Height - top);
            blocks.Add(Finish(Samples.AsSpan(top * rowLength, count * rowLength).ToArray(), rowLength, bigEndian));
        }

        return [.. blocks];
    }

    /// <summary>
    /// Builds the blocks of a page of separate planes from its chunky <see cref="Samples"/>: every block of the first sample,
    /// then the ones of the second sample, and so on, each plane with the geometry, the predictor and the compression of
    /// a page of one sample per pixel.
    /// </summary>
    private byte[][] BuildPlanes(bool bigEndian)
    {
        var bytesPerSample = BitsPerSample / 8;
        var pixelCount = Width * Height;
        var blocks = new List<byte[]>();
        for (var plane = 0; plane < SamplesPerPixel; plane++)
        {
            var samples = new byte[pixelCount * bytesPerSample];
            for (var i = 0; i < pixelCount; i++)
            {
                Samples.AsSpan(((i * SamplesPerPixel) + plane) * bytesPerSample, bytesPerSample).CopyTo(samples.AsSpan(i * bytesPerSample));
            }

            var spec = new TiffPageSpec
            {
                Width = Width,
                Height = Height,
                BitsPerSample = BitsPerSample,
                Compression = Compression,
                RowsPerStrip = RowsPerStrip,
                TileWidth = TileWidth,
                TileLength = TileLength,
                Predictor = Predictor,
                Samples = samples,
            };

            blocks.AddRange(spec.BuildBlocks(bigEndian));
        }

        return [.. blocks];
    }

    private byte[] Finish(byte[] block, int rowLength, bool bigEndian)
    {
        if (Predictor == 2)
        {
            block = ApplyHorizontalDifferencing(block, bigEndian);
        }
        else if (Predictor == 3)
        {
            var predicted = new byte[block.Length];
            for (var offset = 0; offset < block.Length; offset += rowLength)
            {
                TiffReferenceCodecs.ApplyFloatingPointPredictor(block.AsSpan(offset, rowLength), SamplesPerPixel, BitsPerSample / 8, bigEndian).CopyTo(predicted, offset);
            }

            block = predicted;
        }

        return Compression switch
        {
            5 => TiffReferenceCodecs.EncodeLzw(block),
            8 or 32946 => TiffFileBuilder.Deflate(block),
            32773 => TiffReferenceCodecs.EncodePackBits(block, rowLength),
            _ => block,
        };
    }

    private byte[] ApplyHorizontalDifferencing(byte[] block, bool bigEndian)
    {
        var rowLength = (IsTiled ? TileWidth!.Value : Width) * SamplesPerPixel * BitsPerSample / 8;
        var result = (byte[])block.Clone();
        for (var offset = 0; offset < block.Length; offset += rowLength)
        {
            var row = result.AsSpan(offset, rowLength);
            if (BitsPerSample == 8)
            {
                for (var i = row.Length - 1; i >= SamplesPerPixel; i--)
                {
                    row[i] -= row[i - SamplesPerPixel];
                }
            }
            else
            {
                var count = row.Length / 2;
                for (var i = count - 1; i >= SamplesPerPixel; i--)
                {
                    if (bigEndian)
                    {
                        var value = (ushort)(BinaryPrimitives.ReadUInt16BigEndian(row[(i * 2)..]) - BinaryPrimitives.ReadUInt16BigEndian(row[((i - SamplesPerPixel) * 2)..]));
                        BinaryPrimitives.WriteUInt16BigEndian(row[(i * 2)..], value);
                    }
                    else
                    {
                        var value = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(row[(i * 2)..]) - BinaryPrimitives.ReadUInt16LittleEndian(row[((i - SamplesPerPixel) * 2)..]));
                        BinaryPrimitives.WriteUInt16LittleEndian(row[(i * 2)..], value);
                    }
                }
            }
        }

        return result;
    }
}
