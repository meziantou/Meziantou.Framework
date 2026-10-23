namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>The symbol dictionary decoding procedure (T.88, 6.5) and its segment (7.4.2).</summary>
internal static class Jbig2SymbolDictionary
{
    // The new symbols of one dictionary: far more than a page has glyphs
    private const int MaxNewSymbols = 1 << 20;

    /// <summary>Decodes a symbol dictionary segment and returns its exported symbols.</summary>
    /// <param name="data">The data of the stream.</param>
    /// <param name="offset">The start of the segment data.</param>
    /// <param name="length">The length of the segment data.</param>
    /// <param name="inputSymbols">The symbols exported by the dictionaries the segment refers to.</param>
    /// <param name="tables">The custom Huffman tables of the tables segments the segment refers to.</param>
    /// <param name="retained">The contexts retained by the last dictionary the segment refers to, or <see langword="null"/>.</param>
    /// <param name="budget">The budget.</param>
    public static (List<Jbig2Bitmap> Exported, Jbig2RetainedContexts Contexts) Decode(byte[] data, int offset, int length, List<Jbig2Bitmap> inputSymbols, Queue<Jbig2HuffmanTable> tables, Jbig2RetainedContexts? retained, Jbig2Budget budget)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var flags = reader.ReadUInt16();
        var huffman = (flags & 1) != 0;
        var refineAggregate = (flags & 2) != 0;
        var huffmanDeltaHeight = (flags >> 2) & 3;
        var huffmanDeltaWidth = (flags >> 4) & 3;
        var huffmanBitmapSize = (flags >> 6) & 1;
        var huffmanAggregateInstances = (flags >> 7) & 1;
        var contextUsed = (flags & 0x100) != 0;
        var template = (flags >> 10) & 3;
        var refinementTemplate = (flags >> 12) & 1;
        var adaptivePixels = Jbig2GenericRegion.GetDefaultAdaptivePixels(template);
        if (!huffman)
        {
            adaptivePixels = new (int, int)[template == 0 ? 4 : 1];
            for (var i = 0; i < adaptivePixels.Length; i++)
            {
                adaptivePixels[i] = (reader.ReadSByte(), reader.ReadSByte());
            }
        }

        var refinementAdaptivePixels = Jbig2RefinementRegion.GetDefaultAdaptivePixels();
        if (refineAggregate && refinementTemplate == 0)
        {
            refinementAdaptivePixels = [(reader.ReadSByte(), reader.ReadSByte()), (reader.ReadSByte(), reader.ReadSByte())];
        }

        var exportedCount = reader.ReadUInt32();
        var newCount = reader.ReadUInt32();
        if (newCount > MaxNewSymbols || exportedCount > inputSymbols.Count + (long)newCount)
            throw new InvalidPdfContentException("A JBIG2 symbol dictionary has too many symbols");

        // The contexts of the dictionary: those retained by the dictionary it refers to, or new ones (7.4.2.2)
        var contexts = contextUsed && retained is not null && retained.Template == template && retained.RefinementTemplate == refinementTemplate
            ? retained.Clone()
            : new Jbig2RetainedContexts(template, refinementTemplate);

        var symbolCount = inputSymbols.Count + (int)newCount;
        var symbolCodeLength = GetSymbolCodeLength(symbolCount);
        var symbols = new List<Jbig2Bitmap>(symbolCount);
        symbols.AddRange(inputSymbols);
        var newWidths = new List<int>();

        Jbig2ArithmeticDecoder? decoder = null;
        Jbig2TextRegionContexts? textContexts = null;
        Jbig2BitReader? bits = null;
        Jbig2HuffmanTable? deltaHeightTable = null;
        Jbig2HuffmanTable? deltaWidthTable = null;
        Jbig2HuffmanTable? bitmapSizeTable = null;
        Jbig2HuffmanTable? aggregateInstancesTable = null;
        if (huffman)
        {
            deltaHeightTable = SelectTable(huffmanDeltaHeight, [4, 5], tables);
            deltaWidthTable = SelectTable(huffmanDeltaWidth, [2, 3], tables);
            bitmapSizeTable = huffmanBitmapSize == 0 ? Jbig2HuffmanTable.StandardTables[1] : NextTable(tables);
            aggregateInstancesTable = huffmanAggregateInstances == 0 ? Jbig2HuffmanTable.StandardTables[1] : NextTable(tables);
            bits = new Jbig2BitReader(data, reader.Position, reader.Remaining);
        }
        else
        {
            decoder = new Jbig2ArithmeticDecoder(data, reader.Position, reader.Remaining);
            textContexts = new Jbig2TextRegionContexts(decoder, symbolCodeLength);
        }

        var deltaHeightContexts = new byte[512];
        var deltaWidthContexts = new byte[512];
        var exportContexts = new byte[512];
        var aggregateInstancesContexts = new byte[512];
        var height = 0;
        var decoded = 0;
        while (decoded < newCount)
        {
            var deltaHeight = huffman ? deltaHeightTable!.DecodeValue(bits!) : decoder!.DecodeInteger(deltaHeightContexts) ?? throw OutOfBand();
            height += deltaHeight;
            if (height < 0)
                throw new InvalidPdfContentException("A JBIG2 height class has a negative height");

            var width = 0;
            var totalWidth = 0L;
            var firstSymbolOfClass = decoded;
            while (true)
            {
                var deltaWidth = huffman ? deltaWidthTable!.Decode(bits!) : decoder!.DecodeInteger(deltaWidthContexts);
                if (deltaWidth is null)
                    break;

                if (decoded >= newCount)
                    throw new InvalidPdfContentException("A JBIG2 symbol dictionary has more symbols than it declares");

                width += deltaWidth.Value;
                totalWidth += width;
                if (width < 0 || totalWidth > int.MaxValue)
                    throw new InvalidPdfContentException("A JBIG2 symbol has a negative width");

                if (!huffman || refineAggregate)
                {
                    Jbig2Bitmap bitmap;
                    if (!refineAggregate)
                    {
                        bitmap = Jbig2GenericRegion.Decode(decoder!, contexts.Generic, width, height, template, typicalPrediction: false, skip: null, adaptivePixels, budget);
                    }
                    else
                    {
                        bitmap = DecodeAggregate(data, huffman, decoder, textContexts, bits, aggregateInstancesTable, aggregateInstancesContexts, symbols, symbolCodeLength, width, height, refinementTemplate, refinementAdaptivePixels, contexts, tables, budget, length + offset);
                    }

                    symbols.Add(bitmap);
                }
                else
                {
                    newWidths.Add(width);
                }

                decoded++;
            }

            // Huffman coding without refinement: the symbols of the height class are one collective bitmap (6.5.9)
            if (huffman && !refineAggregate)
            {
                var size = bitmapSizeTable!.DecodeValue(bits!);
                bits!.AlignToByte();
                var start = bits.BytePosition;
                if (size < 0 || start + (long)size > offset + length)
                    throw new InvalidPdfContentException("The collective bitmap of a JBIG2 height class is outside its segment");

                Jbig2Bitmap collective;
                if (size == 0)
                {
                    // Uncompressed: the rows as they are
                    collective = new Jbig2Bitmap((int)totalWidth, height, budget);
                    var byteCount = (long)collective.Stride * height;
                    if (start + byteCount > offset + length)
                        throw new InvalidPdfContentException("The collective bitmap of a JBIG2 height class is outside its segment");

                    data.AsSpan(start, (int)byteCount).CopyTo(collective.Data);
                    bits.SkipBytes(byteCount);
                }
                else
                {
                    collective = Jbig2GenericRegion.DecodeMmr(data, start, size, (int)totalWidth, height, budget).Bitmap;
                    bits.SkipBytes(size);
                }

                var x = 0;
                for (var i = firstSymbolOfClass; i < decoded; i++)
                {
                    var symbolWidth = newWidths[i - firstSymbolOfClass];
                    symbols.Add(collective.Extract(x, 0, symbolWidth, height, budget));
                    x += symbolWidth;
                }

                newWidths.Clear();
            }
        }

        // The exported symbols: runs that alternate between not exported and exported (6.5.10)
        var exported = new List<Jbig2Bitmap>((int)exportedCount);
        var index = 0;
        var export = false;
        while (index < symbols.Count)
        {
            var run = huffman ? Jbig2HuffmanTable.StandardTables[1].DecodeValue(bits!) : decoder!.DecodeInteger(exportContexts) ?? throw OutOfBand();
            if (run < 0 || index + (long)run > symbols.Count)
                throw new InvalidPdfContentException("The export flags of a JBIG2 symbol dictionary are invalid");

            if (export)
            {
                exported.AddRange(symbols.GetRange(index, run));
            }

            index += run;
            export = !export;
        }

        return (exported, contexts);
    }

    /// <summary>Decodes a symbol that refines or aggregates other symbols (6.5.8.2).</summary>
    private static Jbig2Bitmap DecodeAggregate(byte[] data, bool huffman, Jbig2ArithmeticDecoder? decoder, Jbig2TextRegionContexts? textContexts, Jbig2BitReader? bits, Jbig2HuffmanTable? aggregateInstancesTable, byte[] aggregateInstancesContexts, List<Jbig2Bitmap> symbols, int symbolCodeLength, int width, int height, int refinementTemplate, (int X, int Y)[] refinementAdaptivePixels, Jbig2RetainedContexts contexts, Queue<Jbig2HuffmanTable> tables, Jbig2Budget budget, int end)
    {
        var instances = huffman ? aggregateInstancesTable!.DecodeValue(bits!) : decoder!.DecodeInteger(aggregateInstancesContexts) ?? throw OutOfBand();
        if (instances < 1)
            throw new InvalidPdfContentException("A JBIG2 aggregate symbol has no instance");

        if (instances == 1)
        {
            // One instance: its identifier, its offset, and a refinement of it (6.5.8.2.2)
            int id;
            int dx;
            int dy;
            Jbig2ArithmeticDecoder refinementDecoder;
            if (huffman)
            {
                id = (int)bits!.ReadBits(symbolCodeLength);
                dx = Jbig2HuffmanTable.StandardTables[15].DecodeValue(bits);
                dy = Jbig2HuffmanTable.StandardTables[15].DecodeValue(bits);
                var size = Jbig2HuffmanTable.StandardTables[1].DecodeValue(bits);
                bits.AlignToByte();
                var start = bits.BytePosition;
                if (size < 0 || start + (long)size > end)
                    throw new InvalidPdfContentException("The refinement data of a JBIG2 symbol is outside its segment");

                refinementDecoder = new Jbig2ArithmeticDecoder(data, start, size);
                bits.SkipBytes(size);
            }
            else
            {
                id = decoder!.DecodeSymbolId(textContexts!.Id, symbolCodeLength);
                dx = decoder.DecodeInteger(textContexts.RefinementX) ?? throw OutOfBand();
                dy = decoder.DecodeInteger(textContexts.RefinementY) ?? throw OutOfBand();
                refinementDecoder = decoder;
            }

            if ((uint)id >= (uint)symbols.Count)
                throw new InvalidPdfContentException("A JBIG2 symbol refines a symbol that does not exist");

            return Jbig2RefinementRegion.Decode(refinementDecoder, contexts.Refinement, width, height, refinementTemplate, symbols[id], dx, dy, typicalPrediction: false, refinementAdaptivePixels, budget);
        }

        // Several instances: a text region of one strip, with refinement (6.5.8.2.1)
        Jbig2TextRegionHuffman? textHuffman = null;
        if (huffman)
        {
            textHuffman = new Jbig2TextRegionHuffman
            {
                Reader = bits!,
                Data = data,
                End = end,
                FirstS = Jbig2HuffmanTable.StandardTables[6],
                DeltaS = Jbig2HuffmanTable.StandardTables[8],
                DeltaT = Jbig2HuffmanTable.StandardTables[11],
                RefinementWidth = Jbig2HuffmanTable.StandardTables[15],
                RefinementHeight = Jbig2HuffmanTable.StandardTables[15],
                RefinementX = Jbig2HuffmanTable.StandardTables[15],
                RefinementY = Jbig2HuffmanTable.StandardTables[15],
                RefinementSize = Jbig2HuffmanTable.StandardTables[1],
                SymbolIds = Jbig2HuffmanTable.CreateFixedLength(symbolCodeLength, symbols.Count),
            };
        }

        var parameters = new Jbig2TextRegionParameters
        {
            Width = width,
            Height = height,
            InstanceCount = instances,
            StripSize = 1,
            Symbols = symbols,
            SymbolCodeLength = symbolCodeLength,
            DefaultPixel = 0,
            CombinationOperator = 0,
            Transposed = false,
            ReferenceCorner = Jbig2ReferenceCorner.TopLeft,
            DsOffset = 0,
            Refine = true,
            RefinementTemplate = refinementTemplate,
            RefinementAdaptivePixels = refinementAdaptivePixels,
            RefinementContexts = contexts.Refinement,
            Arithmetic = textContexts,
            Huffman = textHuffman,
        };
        return Jbig2TextRegion.Decode(parameters, budget);
    }

    /// <summary>Gets the bits of a symbol identifier: the base 2 logarithm of the number of symbols, rounded up (SBSYMCODELEN).</summary>
    internal static int GetSymbolCodeLength(int symbolCount) => symbolCount <= 1 ? 0 : 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)(symbolCount - 1));

    /// <summary>Selects a standard table by the value of its field, or the next custom table for 3.</summary>
    internal static Jbig2HuffmanTable SelectTable(int selection, int[] standardTables, Queue<Jbig2HuffmanTable> tables)
    {
        if (selection == 3)
            return NextTable(tables);

        if (selection >= standardTables.Length)
            throw new InvalidPdfContentException("A JBIG2 segment selects a Huffman table that does not exist");

        return Jbig2HuffmanTable.StandardTables[standardTables[selection]];
    }

    internal static Jbig2HuffmanTable NextTable(Queue<Jbig2HuffmanTable> tables) => tables.TryDequeue(out var table) ? table : throw new InvalidPdfContentException("A JBIG2 segment uses a custom Huffman table it does not refer to");

    private static InvalidPdfContentException OutOfBand() => new("The JBIG2 data has an out-of-band value where a value is expected");
}
