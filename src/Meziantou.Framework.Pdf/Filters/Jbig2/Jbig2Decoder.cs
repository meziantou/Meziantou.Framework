using System.Runtime.InteropServices;

namespace Meziantou.Framework.Pdf.Filters.Jbig2;

/// <summary>
/// Decodes the embedded JBIG2 stream of the <c>JBIG2Decode</c> filter (ISO 32000-2, 7.4.7; ITU-T T.88): the segments of
/// the global stream (<c>/JBIG2Globals</c>) and of the image stream, without file header, which paint one page.
/// </summary>
internal sealed class Jbig2Decoder
{
    // The segments of a stream: far more than a page has
    private const int MaxSegments = 1 << 16;

    private static readonly byte[] FileHeader = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly Jbig2Budget _budget;
    private readonly Dictionary<uint, SegmentResult> _results = [];
    private Jbig2Bitmap? _page;
    private int _pageDefaultPixel;
    private int _pageCombinationOperator;
    private bool _pageOverridesCombination;
    private bool _pageHeightUnknown;
    private int _segmentCount;
    private int _pageMaxStripeSize;

    public Jbig2Decoder(long limit)
    {
        _budget = new Jbig2Budget(limit);
    }

    /// <summary>Creates a decoder that starts with global segments decoded before (<see cref="GetGlobals"/>).</summary>
    /// <remarks>The memory and the work of the global segments are charged again: the limit applies to each image as if it decoded them.</remarks>
    public Jbig2Decoder(long limit, Jbig2Globals globals)
        : this(limit)
    {
        _budget.ChargeMemory(globals.Memory);
        _budget.ChargeWork(globals.Work);
        foreach (var (number, result) in globals.Results)
        {
            _results[number] = result;
        }

        _segmentCount = globals.SegmentCount;
    }

    /// <summary>Gets the budget of the decoding.</summary>
    internal Jbig2Budget Budget => _budget;

    /// <summary>
    /// Gets the results of the segments decoded so far, to start the decoding of other images with them, or
    /// <see langword="null"/> when they painted a page: the bitmaps of the results are never changed after they are
    /// decoded (the contexts a dictionary retains are copied by the dictionaries that use them), but a page is.
    /// </summary>
    public Jbig2Globals? GetGlobals() => _page is null ? new Jbig2Globals(new Dictionary<uint, SegmentResult>(_results), _budget.Memory, _budget.Work, _segmentCount) : null;

    /// <summary>Decodes the segments of a stream: the global segments first, then those of the image.</summary>
    /// <remarks>
    /// Damaged data ends the decoding: the page keeps what the segments before it painted, as viewers show it. Some
    /// files embed a whole JBIG2 file, with its header (D.4): it is read too, in sequential or random-access organization.
    /// </remarks>
    /// <param name="memory">
    /// The data, read only while the method runs. The segments read the array behind it, within its bounds: the data
    /// of a stream is not copied, and no result keeps a reference to it.
    /// </param>
    public void DecodeSegments(ReadOnlyMemory<byte> memory)
    {
        // The array is only read: the data stays read-only
        var isArray = unsafe(MemoryMarshal.TryGetArray(memory, out var segment));
        if (!isArray)
        {
            segment = new ArraySegment<byte>(memory.ToArray());
        }

        var data = segment.Array!;
        var start = segment.Offset;
        var end = start + segment.Count;
        var position = start;
        var randomAccess = false;
        if (segment.AsSpan().StartsWith(FileHeader))
        {
            var flags = segment.Count > 8 ? data[start + 8] : 1;
            randomAccess = (flags & 1) == 0;
            position = start + ((flags & 2) == 0 ? 13 : 9);
        }

        try
        {
            if (randomAccess)
            {
                // The headers of all the segments, then their data in the same order
                var headers = new List<Header>();
                while (position < end)
                {
                    CountSegment();
                    var header = ReadHeader(data, ref position, end);
                    headers.Add(header);
                    if (header.Type == 51)
                        break;
                }

                foreach (var header in headers)
                {
                    var length = (int)Math.Min(header.DataLength, (uint)Math.Max(0, end - position));
                    if (!DecodeSegment(header, data, position, length))
                        break;

                    position += length;
                }

                return;
            }

            while (position < end)
            {
                CountSegment();
                var header = ReadHeader(data, ref position, end);
                var length = header.DataLength;
                if (length == uint.MaxValue)
                {
                    // The length of an immediate generic region may be unknown: its data ends with a marker and a row count (7.2.7)
                    length = FindUnknownLength(data, position, end);
                }

                if (position + (long)length > end)
                {
                    // A truncated last segment is decoded with what it has
                    length = (uint)(end - position);
                }

                if (!DecodeSegment(header, data, position, (int)length))
                    break;

                position += (int)length;
            }
        }
        catch (InvalidPdfContentException) when (_page is not null)
        {
        }
    }

    /// <summary>Gets the page: one bit per pixel, 0 for black and 1 for white as the image data of a PDF file is (7.4.7).</summary>
    /// <remarks>The bits of the page are inverted in place and the page is given to the caller: the decoder has no page after it.</remarks>
    public byte[] GetPage()
    {
        if (_page is null)
            throw new InvalidPdfContentException("The JBIG2 data has no page information segment");

        // The page belongs to this decoder only (the global segments never paint a page that is reused, GetGlobals)
        var result = _page.Data;
        _page = null;
        CcittFaxFilter.Invert(result);
        return result;
    }

    private void CountSegment()
    {
        if (++_segmentCount > MaxSegments)
            throw new PdfResourceLimitException("The JBIG2 data has too many segments");
    }

    private static Header ReadHeader(byte[] data, ref int position, int end)
    {
        var reader = new Jbig2SegmentDataReader(data, position, end - position);
        var number = reader.ReadUInt32();
        var flags = reader.ReadByte();
        var type = flags & 0x3F;
        var pageAssociationIsLong = (flags & 0x40) != 0;
        var first = reader.ReadByte();
        long referredCount = first >> 5;
        if (referredCount == 7)
        {
            // The long form: a 29-bit count, then the retention flags
            referredCount = ((first & 0x1F) << 24) | (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
            var retentionBytes = (referredCount + 8) / 8;
            if (retentionBytes > reader.Remaining)
                throw new InvalidPdfContentException("A JBIG2 segment header is truncated");

            for (var i = 0; i < retentionBytes; i++)
            {
                reader.ReadByte();
            }
        }
        else if (referredCount > 4)
        {
            throw new InvalidPdfContentException("A JBIG2 segment header has an invalid referred-to segment count");
        }

        // Each referred-to segment number takes at least one byte of the header
        if (referredCount > reader.Remaining)
            throw new InvalidPdfContentException("A JBIG2 segment header is truncated");

        var referred = new uint[referredCount];
        for (var i = 0; i < referredCount; i++)
        {
            referred[i] = number <= 256 ? (uint)reader.ReadByte() : number <= 65536 ? (uint)reader.ReadUInt16() : reader.ReadUInt32();
        }

        _ = pageAssociationIsLong ? reader.ReadUInt32() : (uint)reader.ReadByte();
        var dataLength = reader.ReadUInt32();
        position = reader.Position;
        return new Header(number, type, referred, dataLength);
    }

    /// <summary>Finds the end of the data of an immediate generic region of unknown length: the end marker and the row count (7.2.7).</summary>
    private static uint FindUnknownLength(byte[] data, int position, int end)
    {
        if (position + 18 > end)
            return (uint)(end - position);

        var mmr = (data[position + 17] & 1) != 0;
        for (var i = position + 18; i + 6 <= end; i++)
        {
            if ((mmr && data[i] == 0x00 && data[i + 1] == 0x00) || (!mmr && data[i] == 0xFF && data[i + 1] == 0xAC))
                return (uint)(i + 6 - position);
        }

        return (uint)(end - position);
    }

    /// <summary>Decodes a segment.</summary>
    /// <returns><see langword="false"/> at the end of the page or of the file.</returns>
    private bool DecodeSegment(Header header, byte[] data, int offset, int length)
    {
        switch (header.Type)
        {
            case 0:
                {
                    // Symbol dictionary
                    var inputs = new List<Jbig2Bitmap>();
                    Jbig2RetainedContexts? retained = null;
                    foreach (var segment in GetReferred(header))
                    {
                        if (segment.Symbols is not null)
                        {
                            inputs.AddRange(segment.Symbols);
                            retained = segment.Contexts;
                        }
                    }

                    var (exported, contexts) = Jbig2SymbolDictionary.Decode(data, offset, length, inputs, GetTables(header), retained, _budget);
                    _results[header.Number] = new SegmentResult { Symbols = exported, Contexts = contexts };
                    break;
                }

            case 4 or 6 or 7:
                DecodeTextRegion(header, data, offset, length);
                break;

            case 16:
                _results[header.Number] = new SegmentResult { Patterns = Jbig2Halftone.DecodePatterns(data, offset, length, _budget) };
                break;

            case 20 or 22 or 23:
                {
                    var patterns = GetReferred(header).Select(segment => segment.Patterns).FirstOrDefault(patterns => patterns is not null) ?? throw new InvalidPdfContentException("A JBIG2 halftone region does not refer to a pattern dictionary");
                    var (info, bitmap) = Jbig2Halftone.DecodeRegion(data, offset, length, patterns, _budget);
                    PlaceRegion(header, info, bitmap);
                    break;
                }

            case 36 or 38 or 39:
                DecodeGenericRegion(header, data, offset, length);
                break;

            case 40 or 42 or 43:
                DecodeRefinementRegion(header, data, offset, length);
                break;

            case 48:
                DecodePageInformation(data, offset, length);
                break;

            case 49 or 51:
                // End of page, end of file
                return false;

            case 50:
                {
                    // End of stripe: a page whose height was unknown grows to the end of the stripe
                    var reader = new Jbig2SegmentDataReader(data, offset, length);
                    var row = reader.ReadUInt32();
                    if (_page is not null && _pageHeightUnknown && row < int.MaxValue)
                    {
                        _page.GrowHeight((int)row + 1, _pageDefaultPixel, _budget);
                        SetPageWork();
                    }

                    break;
                }

            case 53:
                _results[header.Number] = new SegmentResult { Table = Jbig2HuffmanTable.Read(data, offset, length) };
                break;

            default:
                // Profiles, extensions and the other segments do not change the page
                break;
        }

        return true;
    }

    private void DecodePageInformation(byte[] data, int offset, int length)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var width = reader.ReadDimension();
        var height = reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        var flags = reader.ReadByte();
        _pageDefaultPixel = (flags >> 2) & 1;
        _pageCombinationOperator = (flags >> 3) & 3;
        _pageOverridesCombination = (flags & 0x40) != 0;
        _pageHeightUnknown = height == uint.MaxValue;

        // A page of unknown height grows by stripes of at most the maximum stripe size (7.4.8.6)
        _pageMaxStripeSize = reader.Remaining >= 2 ? reader.ReadUInt16() & 0x7FFF : 0;
        _page = new Jbig2Bitmap(width, _pageHeightUnknown ? 0 : (int)Math.Min(height, int.MaxValue), _budget);
        SetPageWork();
        if (_pageDefaultPixel != 0)
        {
            _page.Fill(1);
        }
    }

    /// <summary>Bounds the work by the size of the page (<see cref="Jbig2Budget.SetPageArea"/>): the page so far and, when its height is unknown, its next stripe.</summary>
    private void SetPageWork()
    {
        if (_page is not null)
        {
            _budget.SetPageArea((long)_page.Width * (_page.Height + (long)(_pageHeightUnknown ? Math.Max(_pageMaxStripeSize, 1) : 0)));
        }
    }

    private void DecodeGenericRegion(Header header, byte[] data, int offset, int length)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var info = reader.ReadRegionInfo();
        if (header.DataLength == uint.MaxValue && length >= 4)
        {
            // The data ends with the number of rows of the region (7.2.7)
            var rows = (uint)((data[offset + length - 4] << 24) | (data[offset + length - 3] << 16) | (data[offset + length - 2] << 8) | data[offset + length - 1]);
            info = info with { Height = (int)Math.Min(rows, (uint)info.Height) };
        }

        var flags = reader.ReadByte();
        var mmr = (flags & 1) != 0;
        var template = (flags >> 1) & 3;
        var typicalPrediction = (flags & 8) != 0;
        Jbig2Bitmap bitmap;
        if (mmr)
        {
            bitmap = Jbig2GenericRegion.DecodeMmr(data, reader.Position, reader.Remaining, info.Width, info.Height, _budget).Bitmap;
        }
        else
        {
            var adaptivePixels = new (int X, int Y)[template == 0 ? 4 : 1];
            for (var i = 0; i < adaptivePixels.Length; i++)
            {
                adaptivePixels[i] = (reader.ReadSByte(), reader.ReadSByte());
            }

            var decoder = new Jbig2ArithmeticDecoder(data, reader.Position, reader.Remaining);
            bitmap = Jbig2GenericRegion.Decode(decoder, new byte[Jbig2GenericRegion.GetContextCount(template)], info.Width, info.Height, template, typicalPrediction, skip: null, adaptivePixels, _budget);
        }

        PlaceRegion(header, info, bitmap);
    }

    private void DecodeRefinementRegion(Header header, byte[] data, int offset, int length)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var info = reader.ReadRegionInfo();
        var flags = reader.ReadByte();
        var template = flags & 1;
        var typicalPrediction = (flags & 2) != 0;
        var adaptivePixels = Jbig2RefinementRegion.GetDefaultAdaptivePixels();
        if (template == 0)
        {
            adaptivePixels = [(reader.ReadSByte(), reader.ReadSByte()), (reader.ReadSByte(), reader.ReadSByte())];
        }

        // The reference is the intermediate region the segment refers to, or the part of the page under the region (7.4.7.5)
        var reference = GetReferred(header).Select(segment => segment.Region).FirstOrDefault(region => region is not null)
            ?? _page?.Extract(info.X, info.Y, info.Width, info.Height, _budget)
            ?? throw new InvalidPdfContentException("A JBIG2 refinement region has no reference");
        var decoder = new Jbig2ArithmeticDecoder(data, reader.Position, reader.Remaining);
        var bitmap = Jbig2RefinementRegion.Decode(decoder, new byte[Jbig2RefinementRegion.GetContextCount(template)], info.Width, info.Height, template, reference, 0, 0, typicalPrediction, adaptivePixels, _budget);
        PlaceRegion(header, info, bitmap);
    }

    private void DecodeTextRegion(Header header, byte[] data, int offset, int length)
    {
        var reader = new Jbig2SegmentDataReader(data, offset, length);
        var info = reader.ReadRegionInfo();
        var flags = reader.ReadUInt16();
        var huffman = (flags & 1) != 0;
        var refine = (flags & 2) != 0;
        var logStrips = (flags >> 2) & 3;
        var referenceCorner = (Jbig2ReferenceCorner)((flags >> 4) & 3);
        var transposed = (flags & 0x40) != 0;
        var combinationOperator = (flags >> 7) & 3;
        var defaultPixel = (flags >> 9) & 1;
        var dsOffset = (flags >> 10) & 0x1F;
        if (dsOffset > 15)
        {
            dsOffset -= 32;
        }

        var refinementTemplate = (flags >> 15) & 1;
        var huffmanFlags = huffman ? reader.ReadUInt16() : 0;
        var refinementAdaptivePixels = Jbig2RefinementRegion.GetDefaultAdaptivePixels();
        if (refine && refinementTemplate == 0)
        {
            refinementAdaptivePixels = [(reader.ReadSByte(), reader.ReadSByte()), (reader.ReadSByte(), reader.ReadSByte())];
        }

        var instanceCount = reader.ReadUInt32();
        var symbols = new List<Jbig2Bitmap>();
        foreach (var segment in GetReferred(header))
        {
            if (segment.Symbols is not null)
            {
                symbols.AddRange(segment.Symbols);
            }
        }

        if (instanceCount > (long)info.Width * info.Height + 1 && instanceCount > 1 << 24)
            throw new PdfResourceLimitException("A JBIG2 text region has too many instances");

        var symbolCodeLength = Jbig2SymbolDictionary.GetSymbolCodeLength(symbols.Count);
        Jbig2TextRegionContexts? arithmetic = null;
        Jbig2TextRegionHuffman? huffmanTables = null;
        if (huffman)
        {
            var tables = GetTables(header);
            var firstS = Jbig2SymbolDictionary.SelectTable(huffmanFlags & 3, [6, 7], tables);
            var deltaS = Jbig2SymbolDictionary.SelectTable((huffmanFlags >> 2) & 3, [8, 9, 10], tables);
            var deltaT = Jbig2SymbolDictionary.SelectTable((huffmanFlags >> 4) & 3, [11, 12, 13], tables);
            var refinementWidth = Jbig2SymbolDictionary.SelectTable((huffmanFlags >> 6) & 3, [14, 15], tables);
            var refinementHeight = Jbig2SymbolDictionary.SelectTable((huffmanFlags >> 8) & 3, [14, 15], tables);
            var refinementX = Jbig2SymbolDictionary.SelectTable((huffmanFlags >> 10) & 3, [14, 15], tables);
            var refinementY = Jbig2SymbolDictionary.SelectTable((huffmanFlags >> 12) & 3, [14, 15], tables);
            var refinementSize = ((huffmanFlags >> 14) & 1) == 0 ? Jbig2HuffmanTable.StandardTables[1] : Jbig2SymbolDictionary.NextTable(tables);
            var bits = new Jbig2BitReader(data, reader.Position, reader.Remaining);
            var symbolIds = Jbig2HuffmanTable.ReadSymbolIdTable(bits, symbols.Count);
            huffmanTables = new Jbig2TextRegionHuffman
            {
                Reader = bits,
                Data = data,
                End = offset + length,
                FirstS = firstS,
                DeltaS = deltaS,
                DeltaT = deltaT,
                RefinementWidth = refinementWidth,
                RefinementHeight = refinementHeight,
                RefinementX = refinementX,
                RefinementY = refinementY,
                RefinementSize = refinementSize,
                SymbolIds = symbolIds,
            };
        }
        else
        {
            arithmetic = new Jbig2TextRegionContexts(new Jbig2ArithmeticDecoder(data, reader.Position, reader.Remaining), symbolCodeLength);
        }

        var parameters = new Jbig2TextRegionParameters
        {
            Width = info.Width,
            Height = info.Height,
            InstanceCount = (int)Math.Min(instanceCount, int.MaxValue),
            StripSize = 1 << logStrips,
            Symbols = symbols,
            SymbolCodeLength = symbolCodeLength,
            DefaultPixel = defaultPixel,
            CombinationOperator = combinationOperator,
            Transposed = transposed,
            ReferenceCorner = referenceCorner,
            DsOffset = dsOffset,
            Refine = refine,
            RefinementTemplate = refinementTemplate,
            RefinementAdaptivePixels = refinementAdaptivePixels,
            RefinementContexts = new byte[Jbig2RefinementRegion.GetContextCount(refinementTemplate)],
            Arithmetic = arithmetic,
            Huffman = huffmanTables,
        };
        PlaceRegion(header, info, Jbig2TextRegion.Decode(parameters, _budget));
    }

    /// <summary>Keeps an intermediate region for the segments that refer to it, or combines an immediate region with the page (7.4.1.5).</summary>
    private void PlaceRegion(Header header, Jbig2RegionInfo info, Jbig2Bitmap bitmap)
    {
        if (header.Type is 4 or 20 or 36 or 40)
        {
            _results[header.Number] = new SegmentResult { Region = bitmap };
            return;
        }

        if (_page is null)
            throw new InvalidPdfContentException("A JBIG2 region comes before the page information segment");

        if (_pageHeightUnknown && info.Y + (long)info.Height > _page.Height)
        {
            _page.GrowHeight((int)Math.Min(info.Y + (long)info.Height, int.MaxValue), _pageDefaultPixel, _budget);
            SetPageWork();
        }

        var combinationOperator = _pageOverridesCombination ? info.CombinationOperator : _pageCombinationOperator;
        _budget.ChargePlacement(bitmap);
        _page.Combine(bitmap, info.X, info.Y, combinationOperator);
    }

    /// <summary>Gets the results of the segments a segment refers to, each once, in the order of the references.</summary>
    private IEnumerable<SegmentResult> GetReferred(Header header)
    {
        var seen = new HashSet<uint>();
        foreach (var number in header.Referred)
        {
            if (seen.Add(number) && _results.TryGetValue(number, out var result))
            {
                // The lists of symbols are copied from the results: their references are charged
                _budget.ChargeMemory(8L * ((result.Symbols?.Count ?? 0) + (result.Patterns?.Count ?? 0)));
                yield return result;
            }
        }
    }

    private Queue<Jbig2HuffmanTable> GetTables(Header header) => new(GetReferred(header).Select(segment => segment.Table).OfType<Jbig2HuffmanTable>());

    private sealed record Header(uint Number, int Type, uint[] Referred, uint DataLength);

    internal sealed class SegmentResult
    {
        public List<Jbig2Bitmap>? Symbols { get; init; }

        public Jbig2RetainedContexts? Contexts { get; init; }

        public List<Jbig2Bitmap>? Patterns { get; init; }

        public Jbig2Bitmap? Region { get; init; }

        public Jbig2HuffmanTable? Table { get; init; }
    }
}
