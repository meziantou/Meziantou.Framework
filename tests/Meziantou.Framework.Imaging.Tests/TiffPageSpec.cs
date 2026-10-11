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

    /// <summary>1 (none), 8 (Adobe Deflate), 32946 (Deflate), or any other value to exercise the rejections.</summary>
    public int Compression { get; init; } = 1;

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
    public byte[][] BuildBlocks()
    {
        var rowLength = Width * SamplesPerPixel * BitsPerSample / 8;
        var blocks = new List<byte[]>();
        if (IsTiled)
        {
            var tileWidth = TileWidth!.Value;
            var tileLength = TileLength!.Value;
            var tileRowLength = tileWidth * SamplesPerPixel * BitsPerSample / 8;
            var pixelLength = SamplesPerPixel * BitsPerSample / 8;
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

                        Samples.AsSpan(((top + y) * rowLength) + (left * pixelLength), columns * pixelLength)
                            .CopyTo(tile.AsSpan(y * tileRowLength));
                    }

                    blocks.Add(Finish(tile));
                }
            }

            return [.. blocks];
        }

        var rows = Math.Min(RowsPerStrip, Height);
        for (var top = 0; top < Height; top += rows)
        {
            var count = Math.Min(rows, Height - top);
            blocks.Add(Finish(Samples.AsSpan(top * rowLength, count * rowLength).ToArray()));
        }

        return [.. blocks];
    }

    private byte[] Finish(byte[] block)
    {
        if (Predictor == 2)
        {
            block = ApplyHorizontalDifferencing(block);
        }

        return Compression is 8 or 32946 ? TiffFileBuilder.Deflate(block) : block;
    }

    private byte[] ApplyHorizontalDifferencing(byte[] block)
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
                    var value = (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(row[(i * 2)..]) - BinaryPrimitives.ReadUInt16LittleEndian(row[((i - SamplesPerPixel) * 2)..]));
                    BinaryPrimitives.WriteUInt16LittleEndian(row[(i * 2)..], value);
                }
            }
        }

        return result;
    }
}
