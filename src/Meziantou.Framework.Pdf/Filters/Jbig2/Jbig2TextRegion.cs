namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The text region decoding procedure (T.88, 6.4): instances of symbols placed along strips.</summary>
internal static class Jbig2TextRegion
{
    /// <summary>Decodes a text region.</summary>
    public static Jbig2Bitmap Decode(Jbig2TextRegionParameters p, Jbig2Budget budget)
    {
        var region = new Jbig2Bitmap(p.Width, p.Height, budget);
        if (p.DefaultPixel != 0)
        {
            region.Fill(1);
        }

        var stripT = -DecodeStripDelta(p);
        var firstS = 0;
        var instances = 0;
        while (instances < p.InstanceCount)
        {
            stripT += DecodeStripDelta(p);
            var first = true;
            var currentS = 0;

            // The instances of the strip end with an out-of-band value, the last strip too (6.4.5): a text region
            // in a symbol dictionary is followed by other data
            while (true)
            {
                if (first)
                {
                    var deltaFirstS = p.Huffman is { } huffman ? huffman.FirstS.DecodeValue(huffman.Reader) : p.Arithmetic!.Decoder.DecodeInteger(p.Arithmetic.FirstS) ?? throw OutOfBand();
                    firstS += deltaFirstS;
                    currentS = firstS;
                    first = false;
                }
                else
                {
                    var deltaS = p.Huffman is { } huffman ? huffman.DeltaS.Decode(huffman.Reader) : p.Arithmetic!.Decoder.DecodeInteger(p.Arithmetic.DeltaS);
                    if (deltaS is null || instances >= p.InstanceCount)
                        break;

                    currentS += deltaS.Value + p.DsOffset;
                }

                var currentT = 0;
                if (p.StripSize > 1)
                {
                    currentT = p.Huffman is { } huffman ? (int)huffman.Reader.ReadBits(p.LogStripSize) : p.Arithmetic!.Decoder.DecodeInteger(p.Arithmetic.T) ?? throw OutOfBand();
                }

                var t = stripT + currentT;
                var id = p.Huffman is { } idHuffman ? idHuffman.SymbolIds.DecodeValue(idHuffman.Reader) : p.Arithmetic!.Decoder.DecodeSymbolId(p.Arithmetic.Id, p.SymbolCodeLength);
                if ((uint)id >= (uint)p.Symbols.Count)
                    throw new InvalidPdfContentException("A JBIG2 text region refers to a symbol that does not exist");

                var refine = p.Refine && (p.Huffman is { } refineHuffman ? refineHuffman.Reader.ReadBit() : p.Arithmetic!.Decoder.DecodeInteger(p.Arithmetic.Refinement) ?? throw OutOfBand()) != 0;
                var symbol = p.Symbols[id];
                if (refine)
                {
                    symbol = DecodeRefinedSymbol(p, symbol, budget);
                }

                budget.ChargeWork(symbol.Width, symbol.Height);
                var width = symbol.Width;
                var height = symbol.Height;
                if (!p.Transposed && p.ReferenceCorner is Jbig2ReferenceCorner.TopRight or Jbig2ReferenceCorner.BottomRight)
                {
                    currentS += width - 1;
                }
                else if (p.Transposed && p.ReferenceCorner is Jbig2ReferenceCorner.BottomLeft or Jbig2ReferenceCorner.BottomRight)
                {
                    currentS += height - 1;
                }

                var s = currentS;
                var (x, y) = p.Transposed ? (t, s) : (s, t);
                switch (p.ReferenceCorner)
                {
                    case Jbig2ReferenceCorner.TopRight:
                        x -= width - 1;
                        break;
                    case Jbig2ReferenceCorner.BottomLeft:
                        y -= height - 1;
                        break;
                    case Jbig2ReferenceCorner.BottomRight:
                        x -= width - 1;
                        y -= height - 1;
                        break;
                }

                region.Combine(symbol, x, y, p.CombinationOperator);
                if (!p.Transposed && p.ReferenceCorner is Jbig2ReferenceCorner.TopLeft or Jbig2ReferenceCorner.BottomLeft)
                {
                    currentS += width - 1;
                }
                else if (p.Transposed && p.ReferenceCorner is Jbig2ReferenceCorner.TopLeft or Jbig2ReferenceCorner.TopRight)
                {
                    currentS += height - 1;
                }

                instances++;
            }
        }

        return region;
    }

    private static int DecodeStripDelta(Jbig2TextRegionParameters p)
    {
        var delta = p.Huffman is { } huffman ? huffman.DeltaT.DecodeValue(huffman.Reader) : p.Arithmetic!.Decoder.DecodeInteger(p.Arithmetic.DeltaT) ?? throw OutOfBand();
        return (int)Math.Clamp((long)delta * p.StripSize, int.MinValue / 2, int.MaxValue / 2);
    }

    /// <summary>Decodes the refinement of a symbol instance (6.4.11): its size and offset changes, then the refined bitmap.</summary>
    private static Jbig2Bitmap DecodeRefinedSymbol(Jbig2TextRegionParameters p, Jbig2Bitmap symbol, Jbig2Budget budget)
    {
        int deltaWidth;
        int deltaHeight;
        int deltaX;
        int deltaY;
        Jbig2ArithmeticDecoder decoder;
        if (p.Huffman is { } huffman)
        {
            deltaWidth = huffman.RefinementWidth.DecodeValue(huffman.Reader);
            deltaHeight = huffman.RefinementHeight.DecodeValue(huffman.Reader);
            deltaX = huffman.RefinementX.DecodeValue(huffman.Reader);
            deltaY = huffman.RefinementY.DecodeValue(huffman.Reader);
            var size = huffman.RefinementSize.DecodeValue(huffman.Reader);
            huffman.Reader.AlignToByte();
            var start = huffman.Reader.BytePosition;
            if (size < 0 || start + (long)size > huffman.End)
                throw new InvalidPdfContentException("The refinement data of a JBIG2 text region is outside its segment");

            decoder = new Jbig2ArithmeticDecoder(huffman.Data, start, size);
            huffman.Reader.SkipBytes(size);
        }
        else
        {
            var arithmetic = p.Arithmetic!;
            deltaWidth = arithmetic.Decoder.DecodeInteger(arithmetic.RefinementWidth) ?? throw OutOfBand();
            deltaHeight = arithmetic.Decoder.DecodeInteger(arithmetic.RefinementHeight) ?? throw OutOfBand();
            deltaX = arithmetic.Decoder.DecodeInteger(arithmetic.RefinementX) ?? throw OutOfBand();
            deltaY = arithmetic.Decoder.DecodeInteger(arithmetic.RefinementY) ?? throw OutOfBand();
            decoder = arithmetic.Decoder;
        }

        var width = (long)symbol.Width + deltaWidth;
        var height = (long)symbol.Height + deltaHeight;
        if (width is < 0 or > int.MaxValue || height is < 0 or > int.MaxValue)
            throw new InvalidPdfContentException("A refined JBIG2 symbol has a negative size");

        // The reference is offset by half the size change, rounded down (6.4.11.3)
        var referenceDx = (int)Math.Floor(deltaWidth / 2.0) + deltaX;
        var referenceDy = (int)Math.Floor(deltaHeight / 2.0) + deltaY;
        return Jbig2RefinementRegion.Decode(decoder, p.RefinementContexts, (int)width, (int)height, p.RefinementTemplate, symbol, referenceDx, referenceDy, typicalPrediction: false, p.RefinementAdaptivePixels, budget);
    }

    private static InvalidPdfContentException OutOfBand() => new("The JBIG2 data has an out-of-band value where a value is expected");
}
