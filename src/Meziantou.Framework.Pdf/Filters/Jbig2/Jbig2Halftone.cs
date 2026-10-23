namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The pattern dictionary (T.88, 6.7 and 7.4.4) and halftone region (6.6 and 7.4.5) decoding procedures.</summary>
internal static class Jbig2Halftone
{
    // The patterns of a dictionary, and the bits per gray-scale value they allow
    private const int MaxPatterns = 1 << 16;

    /// <summary>Decodes a pattern dictionary segment: patterns of one size, cut from one collective bitmap.</summary>
    public static List<Jbig2Bitmap> DecodePatterns(byte[] data, int offset, int length, Jbig2Budget budget)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var flags = reader.ReadByte();
        var mmr = (flags & 1) != 0;
        var template = (flags >> 1) & 3;
        var patternWidth = reader.ReadByte();
        var patternHeight = reader.ReadByte();
        var grayMax = reader.ReadUInt32();
        if (grayMax >= MaxPatterns || patternWidth == 0 || patternHeight == 0)
            throw new InvalidPdfContentException("A JBIG2 pattern dictionary is invalid");

        var count = (int)grayMax + 1;
        var width = count * patternWidth;
        Jbig2Bitmap collective;
        if (mmr)
        {
            collective = Jbig2GenericRegion.DecodeMmr(data, reader.Position, reader.Remaining, width, patternHeight, budget).Bitmap;
        }
        else
        {
            // The first adaptive pixel is in the previous pattern (6.7.5)
            (int X, int Y)[] adaptivePixels = template == 0 ? [(-patternWidth, 0), (-3, -1), (2, -2), (-2, -2)] : [(-patternWidth, 0)];
            var decoder = new Jbig2ArithmeticDecoder(data, reader.Position, reader.Remaining);
            collective = Jbig2GenericRegion.Decode(decoder, new byte[Jbig2GenericRegion.GetContextCount(template)], width, patternHeight, template, typicalPrediction: false, skip: null, adaptivePixels, budget);
        }

        var patterns = new List<Jbig2Bitmap>(count);
        for (var i = 0; i < count; i++)
        {
            patterns.Add(collective.Extract(i * patternWidth, 0, patternWidth, patternHeight, budget));
        }

        return patterns;
    }

    /// <summary>Decodes a halftone region segment: a grid of gray-scale values, each painted with its pattern.</summary>
    public static (Jbig2RegionInfo Info, Jbig2Bitmap Bitmap) DecodeRegion(byte[] data, int offset, int length, List<Jbig2Bitmap> patterns, Jbig2Budget budget)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var info = reader.ReadRegionInfo();
        var flags = reader.ReadByte();
        var mmr = (flags & 1) != 0;
        var template = (flags >> 1) & 3;
        var enableSkip = (flags & 8) != 0;
        var combinationOperator = (flags >> 4) & 7;
        var defaultPixel = (flags >> 7) & 1;
        var gridWidth = reader.ReadDimension();
        var gridHeight = reader.ReadDimension();
        var gridX = reader.ReadInt32();
        var gridY = reader.ReadInt32();
        var stepX = reader.ReadUInt16();
        var stepY = reader.ReadUInt16();
        if (patterns.Count == 0)
            throw new InvalidPdfContentException("A JBIG2 halftone region has no pattern");

        budget.ChargeWork(gridWidth, gridHeight);
        var region = new Jbig2Bitmap(info.Width, info.Height, budget);
        if (defaultPixel != 0)
        {
            region.Fill(1);
        }

        var patternWidth = patterns[0].Width;
        var patternHeight = patterns[0].Height;

        // The position of a cell of the grid, in 1/256 pixel (6.6.5.2)
        (int X, int Y) CellPosition(int m, int n)
        {
            var x = ((long)gridX + ((long)m * stepY) + ((long)n * stepX)) >> 8;
            var y = ((long)gridY + ((long)m * stepX) - ((long)n * stepY)) >> 8;
            return ((int)Math.Clamp(x, int.MinValue / 2, int.MaxValue / 2), (int)Math.Clamp(y, int.MinValue / 2, int.MaxValue / 2));
        }

        Jbig2Bitmap? skip = null;
        if (enableSkip)
        {
            // The cells whose pattern would be outside the region are not coded (6.6.5.1)
            skip = new Jbig2Bitmap(gridWidth, gridHeight, budget);
            for (var m = 0; m < gridHeight; m++)
            {
                for (var n = 0; n < gridWidth; n++)
                {
                    var (x, y) = CellPosition(m, n);
                    if (x + patternWidth <= 0 || x >= info.Width || y + patternHeight <= 0 || y >= info.Height)
                    {
                        skip.SetPixel(n, m, 1);
                    }
                }
            }
        }

        // The gray-scale values: bit planes coded in Gray code, the most significant one first (C.5)
        var bitsPerValue = Jbig2SymbolDictionary.GetSymbolCodeLength(patterns.Count);
        budget.ChargeMemory((long)gridWidth * gridHeight * sizeof(int));
        var values = new int[gridWidth * gridHeight];
        if (bitsPerValue > 0)
        {
            (int X, int Y)[] adaptivePixels = template == 0 ? [(3, -1), (-3, -1), (2, -2), (-2, -2)] : template == 1 ? [(3, -1)] : [(2, -1)];
            var contexts = new byte[Jbig2GenericRegion.GetContextCount(template)];
            var decoder = mmr ? null : new Jbig2ArithmeticDecoder(data, reader.Position, reader.Remaining);
            var position = reader.Position;
            Jbig2Bitmap? previous = null;
            for (var plane = bitsPerValue - 1; plane >= 0; plane--)
            {
                Jbig2Bitmap bits;
                if (mmr)
                {
                    var (bitmap, used) = Jbig2GenericRegion.DecodeMmr(data, position, offset + length - position, gridWidth, gridHeight, budget);
                    bits = bitmap;
                    position += used;
                }
                else
                {
                    bits = Jbig2GenericRegion.Decode(decoder!, contexts, gridWidth, gridHeight, template, typicalPrediction: false, skip, adaptivePixels, budget);
                }

                if (previous is not null)
                {
                    for (var i = 0; i < bits.Data.Length; i++)
                    {
                        bits.Data[i] ^= previous.Data[i];
                    }
                }

                for (var m = 0; m < gridHeight; m++)
                {
                    for (var n = 0; n < gridWidth; n++)
                    {
                        values[(m * gridWidth) + n] |= bits.GetPixel(n, m) << plane;
                    }
                }

                previous = bits;
            }
        }

        for (var m = 0; m < gridHeight; m++)
        {
            for (var n = 0; n < gridWidth; n++)
            {
                if (skip?.GetPixel(n, m) == 1)
                    continue;

                var (x, y) = CellPosition(m, n);
                var value = Math.Min(values[(m * gridWidth) + n], patterns.Count - 1);
                budget.ChargeWork(patternWidth, patternHeight);
                region.Combine(patterns[value], x, y, combinationOperator);
            }
        }

        return (info, region);
    }
}
