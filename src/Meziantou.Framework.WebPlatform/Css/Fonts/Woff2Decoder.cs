using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>
/// Decodes a WOFF 2.0 file to its sfnt data (https://www.w3.org/TR/WOFF2/): the tables are compressed together with Brotli,
/// and the <c>glyf</c>, <c>loca</c> and <c>hmtx</c> tables may be transformed. A file that does not conform to the
/// requirements of the format (https://www.w3.org/TR/WOFF2/#conform-mustRejectInvalid) is rejected.
/// </summary>
internal static class Woff2Decoder
{
    private const int HeaderLength = 48;

    // https://www.w3.org/TR/WOFF2/#table_dir_format: the tags of the known tables, by index
    private static readonly uint[] KnownTags =
    [
        Tag("cmap"), Tag("head"), Tag("hhea"), Tag("hmtx"), Tag("maxp"), Tag("name"), Tag("OS/2"), Tag("post"), Tag("cvt "), Tag("fpgm"), Tag("glyf"), Tag("loca"), Tag("prep"),
        Tag("CFF "), Tag("VORG"), Tag("EBDT"), Tag("EBLC"), Tag("gasp"), Tag("hdmx"), Tag("kern"), Tag("LTSH"), Tag("PCLT"), Tag("VDMX"), Tag("vhea"), Tag("vmtx"), Tag("BASE"),
        Tag("GDEF"), Tag("GPOS"), Tag("GSUB"), Tag("EBSC"), Tag("JSTF"), Tag("MATH"), Tag("CBDT"), Tag("CBLC"), Tag("COLR"), Tag("CPAL"), Tag("SVG "), Tag("sbix"), Tag("acnt"),
        Tag("avar"), Tag("bdat"), Tag("bloc"), Tag("bsln"), Tag("cvar"), Tag("fdsc"), Tag("feat"), Tag("fmtx"), Tag("fvar"), Tag("gvar"), Tag("hsty"), Tag("just"), Tag("lcar"),
        Tag("mort"), Tag("morx"), Tag("opbd"), Tag("prop"), Tag("trak"), Tag("Zapf"), Tag("Silf"), Tag("Glat"), Tag("Gloc"), Tag("Feat"), Tag("Sill"),
    ];

    private static uint Tag(string value) => OpenTypeTag.TryParse(value, out var tag) ? tag : 0;

    private struct TableEntry
    {
        public uint Tag;
        public bool IsTransformed;
        public uint OriginalLength;
        public int SourceOffset;
        public int SourceLength;
        public ReadOnlyMemory<byte> Output;
        public bool IsReconstructed;
    }

    /// <summary>Returns the sfnt data of the WOFF2 file (a font, or a font collection), or null when the file is invalid.</summary>
    public static byte[]? Decode(ReadOnlySpan<byte> data)
    {
        // https://www.w3.org/TR/WOFF2/#woff20Header
        if (data.Length < HeaderLength || FontBinary.U32(data, 0) != OpenTypeTag.Woff2 || FontBinary.U32(data, 8) != data.Length)
            return null;

        var flavor = FontBinary.U32(data, 4);
        var tableCount = FontBinary.U16(data, 12);
        var compressedLength = FontBinary.U32(data, 20);
        // The reserved field (at 14) is ignored, as in the browsers (WPT css/WOFF2/header-reserved-001.xht)
        if (tableCount == 0 || compressedLength == 0)
            return null;

        // https://www.w3.org/TR/WOFF2/#table_dir_format
        var position = HeaderLength;
        var tables = new TableEntry[tableCount];
        var decompressedLength = 0L;
        for (var i = 0; i < tableCount; i++)
        {
            if (position >= data.Length)
                return null;

            var flags = data[position++];
            uint tag;
            if ((flags & 0x3F) == 0x3F)
            {
                if (!FontBinary.IsInRange(data, position, 4))
                    return null;

                tag = FontBinary.U32(data, position);
                position += 4;
            }
            else
            {
                tag = KnownTags[flags & 0x3F];
            }

            // The transformation version 0 of glyf and loca is their transform, and 3 their null transform; the version 0
            // of the other tables is their null transform
            var version = flags >> 6;
            var isTransformed = tag is OpenTypeTag.Glyf or OpenTypeTag.Loca ? version == 0 : version != 0;
            if (!ReadUIntBase128(data, ref position, out var originalLength))
                return null;

            var sourceLength = originalLength;
            if (isTransformed)
            {
                if (!ReadUIntBase128(data, ref position, out sourceLength))
                    return null;

                // The only transforms are the ones of glyf, loca (whose transformed length is 0) and hmtx (version 1)
                if ((tag == OpenTypeTag.Loca && sourceLength != 0) || (tag == OpenTypeTag.Hmtx && version != 1) || tag is not (OpenTypeTag.Glyf or OpenTypeTag.Loca or OpenTypeTag.Hmtx))
                    return null;
            }

            if (originalLength > FontFile.MaxDecodedLength)
                return null;

            tables[i] = new TableEntry { Tag = tag, IsTransformed = isTransformed, OriginalLength = originalLength, SourceOffset = (int)decompressedLength, SourceLength = (int)sourceLength };
            decompressedLength += sourceLength;
            if (decompressedLength > FontFile.MaxDecodedLength)
                return null;
        }

        // https://www.w3.org/TR/WOFF2/#collection_dir_format
        List<(uint Flavor, int[] Tables)> fonts;
        var isCollection = flavor == OpenTypeTag.Collection;
        if (isCollection)
        {
            var collectionVersion = FontBinary.U32(data, position);
            position += 4;
            if (collectionVersion is not (0x00010000 or 0x00020000) || !Read255UInt16(data, ref position, out var fontCount) || fontCount == 0)
                return null;

            fonts = new List<(uint Flavor, int[] Tables)>(fontCount);
            for (var i = 0; i < fontCount; i++)
            {
                if (!Read255UInt16(data, ref position, out var fontTableCount) || fontTableCount == 0 || !FontBinary.IsInRange(data, position, 4))
                    return null;

                var fontFlavor = FontBinary.U32(data, position);
                position += 4;
                var indexes = new int[fontTableCount];
                for (var j = 0; j < fontTableCount; j++)
                {
                    if (!Read255UInt16(data, ref position, out var index) || index >= tableCount)
                        return null;

                    indexes[j] = index;
                }

                // https://www.w3.org/TR/WOFF2/#collection_dir_format: "the glyf table and the loca table of a font must be
                // [...] immediately following each other" in the table directory (a pair of tables of two fonts is mismatched)
                var glyfIndex = -1;
                var locaIndex = -1;
                foreach (var index in indexes)
                {
                    if (tables[index].Tag == OpenTypeTag.Glyf)
                    {
                        glyfIndex = index;
                    }
                    else if (tables[index].Tag == OpenTypeTag.Loca)
                    {
                        locaIndex = index;
                    }
                }

                if ((glyfIndex >= 0 || locaIndex >= 0) && locaIndex != glyfIndex + 1)
                    return null;

                fonts.Add((fontFlavor, indexes));
            }
        }
        else
        {
            var indexes = new int[tableCount];
            for (var i = 0; i < tableCount; i++)
            {
                indexes[i] = i;
            }

            fonts = [(flavor, indexes)];
        }

        // The compressed data follows the directories; the metadata and the private data blocks follow it
        if (!FontBinary.IsInRange(data, position, compressedLength) || !IsValidBlock(data, FontBinary.U32(data, 28), FontBinary.U32(data, 32)) || !IsValidBlock(data, FontBinary.U32(data, 40), FontBinary.U32(data, 44)))
            return null;

        // https://www.w3.org/TR/WOFF2/#conform-extraneous-reject: "the user agent must reject the file" that has extraneous data
        // "between the data blocks or after the last block", or whose blocks overlap: each block starts at the first 4-byte
        // boundary after the previous one, and the file ends with the last one (as the reference decoder of the format)
        var end = Round4((long)position + compressedLength);
        foreach (var (offset, length) in ((uint, uint)[])[(FontBinary.U32(data, 28), FontBinary.U32(data, 32)), (FontBinary.U32(data, 40), FontBinary.U32(data, 44))])
        {
            if (offset == 0)
                continue;

            if (offset != end)
                return null;

            end = Round4((long)offset + length);
        }

        if (end != Round4(data.Length))
            return null;

        if (DecompressBrotli(data.Slice(position, (int)compressedLength), (int)decompressedLength) is not { } decompressed)
            return null;

        var xMinsByGlyf = new Dictionary<int, short[]>();
        foreach (var (_, indexes) in fonts)
        {
            if (!ReconstructFont(decompressed, tables, indexes, xMinsByGlyf))
                return null;
        }

        var outputTables = new List<(uint Tag, ReadOnlyMemory<byte> Data)>(tableCount);
        foreach (var table in tables)
        {
            outputTables.Add((table.Tag, table.Output));
        }

        return isCollection ? SfntWriter.WriteCollection(fonts, outputTables) : SfntWriter.WriteFont(flavor, outputTables);
    }

    private static bool IsValidBlock(ReadOnlySpan<byte> data, uint offset, uint length) => offset == 0 ? length == 0 : FontBinary.IsInRange(data, offset, length);

    private static long Round4(long value) => (value + 3) & ~3L;

    /// <summary>Decompresses the Brotli stream, whose decompressed length must be the sum of the lengths of the tables.</summary>
    private static byte[]? DecompressBrotli(ReadOnlySpan<byte> compressed, int length)
    {
        // The output grows with the decompressed data, so that a small file cannot allocate the length it declares
        var output = new byte[Math.Min(length, Math.Max(compressed.Length * 4, 4096))];
        var written = 0;
        using var decoder = new BrotliDecoder();
        while (true)
        {
            var status = decoder.Decompress(compressed, output.AsSpan(written), out var consumed, out var count);
            compressed = compressed[consumed..];
            written += count;
            switch (status)
            {
                case OperationStatus.Done:
                    return written == length && compressed.IsEmpty ? output : null;

                case OperationStatus.DestinationTooSmall:
                    if (output.Length >= length)
                        return null;

                    Array.Resize(ref output, (int)Math.Min(length, output.Length * 2L));
                    break;

                default:
                    return null;
            }
        }
    }

    /// <summary>Reconstructs the tables of a font of the file (the tables shared with the previous fonts of a collection are already reconstructed).</summary>
    private static bool ReconstructFont(byte[] decompressed, TableEntry[] tables, int[] indexes, Dictionary<int, short[]> xMinsByGlyf)
    {
        int glyf = -1, loca = -1, hmtx = -1, hhea = -1, maxp = -1, head = -1;
        foreach (var index in indexes)
        {
            switch (tables[index].Tag)
            {
                case OpenTypeTag.Glyf: glyf = index; break;
                case OpenTypeTag.Loca: loca = index; break;
                case OpenTypeTag.Hmtx: hmtx = index; break;
                case OpenTypeTag.Hhea: hhea = index; break;
                case OpenTypeTag.Maxp: maxp = index; break;
                case OpenTypeTag.Head: head = index; break;
            }
        }

        // glyf and loca are both present or both absent, and are both transformed or not
        if ((glyf < 0) != (loca < 0) || (glyf >= 0 && tables[glyf].IsTransformed != tables[loca].IsTransformed))
            return false;

        foreach (var index in indexes)
        {
            ref var table = ref tables[index];
            if (table.IsReconstructed || table.IsTransformed)
                continue;

            if (table.SourceLength != table.OriginalLength)
                return false;

            table.Output = decompressed.AsMemory(table.SourceOffset, table.SourceLength);
            table.IsReconstructed = true;
        }

        short[]? xMins = null;
        if (glyf >= 0 && tables[glyf].IsTransformed)
        {
            if (!tables[glyf].IsReconstructed)
            {
                var source = decompressed.AsSpan(tables[glyf].SourceOffset, tables[glyf].SourceLength);
                if (!ReconstructGlyf(source, (int)tables[glyf].OriginalLength, out var glyfData, out var locaData, out xMins) || locaData.Length != tables[loca].OriginalLength)
                    return false;

                // The format of loca is the one of the head table
                if (head < 0 || FontBinary.I16(tables[head].Output.Span, 50) != (locaData.Length == (xMins.Length + 1) * 4 ? 1 : 0))
                    return false;

                tables[glyf].Output = glyfData;
                tables[glyf].IsReconstructed = true;
                tables[loca].Output = locaData;
                tables[loca].IsReconstructed = true;
                xMinsByGlyf[glyf] = xMins;
            }
            else if (!xMinsByGlyf.TryGetValue(glyf, out xMins))
            {
                return false;
            }
        }

        if (hmtx >= 0 && tables[hmtx].IsTransformed && !tables[hmtx].IsReconstructed)
        {
            // https://www.w3.org/TR/WOFF2/#hmtx_table_format: the left side bearings may be the xMin of the glyphs
            if (xMins is null || hhea < 0 || maxp < 0)
                return false;

            var glyphCount = FontBinary.U16(tables[maxp].Output.Span, 4);
            var metricsCount = FontBinary.U16(tables[hhea].Output.Span, 34);
            var source = decompressed.AsSpan(tables[hmtx].SourceOffset, tables[hmtx].SourceLength);
            if (glyphCount != xMins.Length || ReconstructHmtx(source, glyphCount, metricsCount, xMins) is not { } hmtxData)
                return false;

            tables[hmtx].Output = hmtxData;
            tables[hmtx].IsReconstructed = true;
        }

        foreach (var index in indexes)
        {
            if (!tables[index].IsReconstructed)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Reconstructs the glyf and loca tables from the transformed glyf table (https://www.w3.org/TR/WOFF2/#glyf_table_format),
    /// as the reference decoder does (the simple glyphs are written with the compact encoding of the flags and coordinates).
    /// </summary>
    private static bool ReconstructGlyf(ReadOnlySpan<byte> source, int originalLength, out byte[] glyf, out byte[] loca, out short[] xMins)
    {
        glyf = [];
        loca = [];
        xMins = [];
        const int TransformHeaderLength = 36;
        if (source.Length < TransformHeaderLength)
            return false;

        var optionFlags = FontBinary.U16(source, 2);
        var glyphCount = FontBinary.U16(source, 4);
        var indexFormat = FontBinary.U16(source, 6);
        if (indexFormat > 1)
            return false;

        // The streams follow the header, in order
        Span<int> starts = stackalloc int[8];
        Span<int> ends = stackalloc int[8];
        long offset = TransformHeaderLength;
        for (var i = 0; i < 7; i++)
        {
            var length = FontBinary.U32(source, 8 + (i * 4));
            if (!FontBinary.IsInRange(source, offset, length))
                return false;

            starts[i] = (int)offset;
            ends[i] = (int)(offset + length);
            offset += length;
        }

        const int ContourStream = 0, PointStream = 1, FlagStream = 2, GlyphStream = 3, CompositeStream = 4, BboxStream = 5, InstructionStream = 6;
        var hasOverlapBitmap = (optionFlags & 1) != 0;
        var overlapBitmapStart = (int)offset;
        if (hasOverlapBitmap && !FontBinary.IsInRange(source, offset, (glyphCount + 7) >> 3))
            return false;

        // The bounding box stream starts with the bitmap of the glyphs whose bounding box is explicit
        var bboxBitmapLength = ((glyphCount + 31) >> 5) << 2;
        if (ends[BboxStream] - starts[BboxStream] < bboxBitmapLength || ends[ContourStream] - starts[ContourStream] < glyphCount * 2)
            return false;

        var bboxBitmap = starts[BboxStream];
        starts[BboxStream] += bboxBitmapLength;

        var output = new GlyfWriter(Math.Max(originalLength, 64));
        var offsets = new uint[glyphCount + 1];
        xMins = new short[glyphCount];
        var points = ArrayPool<int>.Shared.Rent(64);
        Span<byte> bbox = stackalloc byte[8];
        try
        {
            for (var glyph = 0; glyph < glyphCount; glyph++)
            {
                output.Pad4();
                offsets[glyph] = (uint)output.Length;
                var contourCount = FontBinary.I16(source, starts[ContourStream] + (glyph * 2));
                var hasBbox = (source[bboxBitmap + (glyph >> 3)] & (0x80 >> (glyph & 7))) != 0;
                if (contourCount == 0)
                {
                    // An empty glyph has no bounding box
                    if (hasBbox)
                        return false;
                }
                else if (contourCount == -1)
                {
                    // https://www.w3.org/TR/WOFF2/#glyf_table_format: a composite glyph has an explicit bounding box
                    if (!hasBbox || !TryGetCompositeLength(source, starts[CompositeStream], ends[CompositeStream], out var compositeLength, out var hasInstructions) || !FontBinary.IsInRange(source, starts[BboxStream], 8) || starts[BboxStream] + 8 > ends[BboxStream])
                        return false;

                    output.WriteI16(-1);
                    output.Write(source.Slice(starts[BboxStream], 8));
                    xMins[glyph] = FontBinary.I16(source, starts[BboxStream]);
                    starts[BboxStream] += 8;
                    output.Write(source.Slice(starts[CompositeStream], compositeLength));
                    starts[CompositeStream] += compositeLength;
                    if (hasInstructions && !WriteInstructions(source, ref starts[GlyphStream], ends[GlyphStream], ref starts[InstructionStream], ends[InstructionStream], ref output))
                        return false;
                }
                else if (contourCount > 0)
                {
                    // The number of points of each contour, then the flags and the coordinates of the points
                    var headerStart = output.Length;
                    output.WriteI16(contourCount);
                    output.Skip(8);
                    var pointCount = 0;
                    for (var contour = 0; contour < contourCount; contour++)
                    {
                        if (!Read255UInt16(source[..ends[PointStream]], ref starts[PointStream], out var contourPointCount))
                            return false;

                        pointCount += contourPointCount;
                        if (pointCount > ushort.MaxValue)
                            return false;

                        output.WriteU16((ushort)(pointCount - 1));
                    }

                    if (starts[FlagStream] + pointCount > ends[FlagStream])
                        return false;

                    if (points.Length < pointCount * 3)
                    {
                        ArrayPool<int>.Shared.Return(points);
                        points = ArrayPool<int>.Shared.Rent(pointCount * 3);
                    }

                    int x = 0, y = 0, xMin = 0, yMin = 0, xMax = 0, yMax = 0;
                    for (var point = 0; point < pointCount; point++)
                    {
                        var flag = source[starts[FlagStream]++];
                        if (!DecodeTriplet(source[..ends[GlyphStream]], ref starts[GlyphStream], flag, out var dx, out var dy))
                            return false;

                        x += dx;
                        y += dy;
                        points[point * 3] = x;
                        points[(point * 3) + 1] = y;
                        points[(point * 3) + 2] = (flag & 0x80) == 0 ? 1 : 0;
                        if (point == 0)
                        {
                            xMin = xMax = x;
                            yMin = yMax = y;
                        }
                        else
                        {
                            xMin = Math.Min(xMin, x);
                            xMax = Math.Max(xMax, x);
                            yMin = Math.Min(yMin, y);
                            yMax = Math.Max(yMax, y);
                        }
                    }

                    if (!WriteInstructions(source, ref starts[GlyphStream], ends[GlyphStream], ref starts[InstructionStream], ends[InstructionStream], ref output))
                        return false;

                    if (hasBbox)
                    {
                        if (starts[BboxStream] + 8 > ends[BboxStream])
                            return false;

                        output.Patch(headerStart + 2, source.Slice(starts[BboxStream], 8));
                        xMins[glyph] = FontBinary.I16(source, starts[BboxStream]);
                        starts[BboxStream] += 8;
                    }
                    else
                    {
                        BinaryPrimitives.WriteInt16BigEndian(bbox, (short)xMin);
                        BinaryPrimitives.WriteInt16BigEndian(bbox[2..], (short)yMin);
                        BinaryPrimitives.WriteInt16BigEndian(bbox[4..], (short)xMax);
                        BinaryPrimitives.WriteInt16BigEndian(bbox[6..], (short)yMax);
                        output.Patch(headerStart + 2, bbox);
                        xMins[glyph] = (short)xMin;
                    }

                    var overlap = hasOverlapBitmap && (source[overlapBitmapStart + (glyph >> 3)] & (0x80 >> (glyph & 7))) != 0;
                    output.WritePoints(points.AsSpan(0, pointCount * 3), overlap);
                }
                else
                {
                    return false;
                }
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(points);
        }

        output.Pad4();
        offsets[glyphCount] = (uint)output.Length;
        if (indexFormat == 0 && output.Length > 0x1FFFE)
            return false;

        glyf = output.ToArray();
        loca = new byte[(glyphCount + 1) * (indexFormat == 0 ? 2 : 4)];
        for (var i = 0; i <= glyphCount; i++)
        {
            if (indexFormat == 0)
            {
                BinaryPrimitives.WriteUInt16BigEndian(loca.AsSpan(i * 2), (ushort)(offsets[i] >> 1));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(i * 4), offsets[i]);
            }
        }

        return true;
    }

    /// <summary>Copies the instructions of a glyph: their length is in the glyph stream and their bytes in the instruction stream.</summary>
    private static bool WriteInstructions(ReadOnlySpan<byte> source, ref int glyphStream, int glyphStreamEnd, ref int instructionStream, int instructionStreamEnd, ref GlyfWriter output)
    {
        if (!Read255UInt16(source[..glyphStreamEnd], ref glyphStream, out var length) || instructionStream + length > instructionStreamEnd)
            return false;

        output.WriteU16((ushort)length);
        output.Write(source.Slice(instructionStream, length));
        instructionStream += length;
        return true;
    }

    /// <summary>The length of the components of a composite glyph (https://learn.microsoft.com/typography/opentype/spec/glyf#composite-glyph-description).</summary>
    private static bool TryGetCompositeLength(ReadOnlySpan<byte> source, int start, int end, out int length, out bool hasInstructions)
    {
        const ushort Arg1And2AreWords = 0x0001, WeHaveAScale = 0x0008, MoreComponents = 0x0020, WeHaveAnXAndYScale = 0x0040, WeHaveATwoByTwo = 0x0080, WeHaveInstructions = 0x0100;
        hasInstructions = false;
        var position = start;
        ushort flags;
        do
        {
            if (position + 4 > end)
            {
                length = 0;
                return false;
            }

            flags = FontBinary.U16(source, position);
            hasInstructions |= (flags & WeHaveInstructions) != 0;
            var argumentsLength = 4 + ((flags & Arg1And2AreWords) != 0 ? 4 : 2);
            if ((flags & WeHaveAScale) != 0)
            {
                argumentsLength += 2;
            }
            else if ((flags & WeHaveAnXAndYScale) != 0)
            {
                argumentsLength += 4;
            }
            else if ((flags & WeHaveATwoByTwo) != 0)
            {
                argumentsLength += 8;
            }

            position += argumentsLength;
        }
        while ((flags & MoreComponents) != 0);

        length = position - start;
        return position <= end;
    }

    /// <summary>Decodes the coordinates of a point (https://www.w3.org/TR/WOFF2/#triplet_decoding).</summary>
    private static bool DecodeTriplet(ReadOnlySpan<byte> stream, ref int position, byte flag, out int dx, out int dy)
    {
        flag &= 0x7F;
        var byteCount = flag switch
        {
            < 84 => 1,
            < 120 => 2,
            < 124 => 3,
            _ => 4,
        };

        dx = dy = 0;
        if (position + byteCount > stream.Length)
            return false;

        var data = stream.Slice(position, byteCount);
        position += byteCount;
        if (flag < 10)
        {
            dy = WithSign(flag, ((flag & 14) << 7) + data[0]);
        }
        else if (flag < 20)
        {
            dx = WithSign(flag, (((flag - 10) & 14) << 7) + data[0]);
        }
        else if (flag < 84)
        {
            var b0 = flag - 20;
            var b1 = data[0];
            dx = WithSign(flag, 1 + (b0 & 0x30) + (b1 >> 4));
            dy = WithSign(flag >> 1, 1 + ((b0 & 0x0C) << 2) + (b1 & 0x0F));
        }
        else if (flag < 120)
        {
            var b0 = flag - 84;
            dx = WithSign(flag, 1 + ((b0 / 12) << 8) + data[0]);
            dy = WithSign(flag >> 1, 1 + (((b0 % 12) >> 2) << 8) + data[1]);
        }
        else if (flag < 124)
        {
            var b2 = data[1];
            dx = WithSign(flag, (data[0] << 4) + (b2 >> 4));
            dy = WithSign(flag >> 1, ((b2 & 0x0F) << 8) + data[2]);
        }
        else
        {
            dx = WithSign(flag, (data[0] << 8) + data[1]);
            dy = WithSign(flag >> 1, (data[2] << 8) + data[3]);
        }

        return true;

        static int WithSign(int flag, int value) => (flag & 1) != 0 ? value : -value;
    }

    /// <summary>Reconstructs the hmtx table from its transformed version (https://www.w3.org/TR/WOFF2/#hmtx_table_format).</summary>
    private static byte[]? ReconstructHmtx(ReadOnlySpan<byte> source, int glyphCount, int metricsCount, short[] xMins)
    {
        if (source.Length < 1)
            return null;

        var flags = source[0];
        var hasProportionalBearings = (flags & 1) == 0;
        var hasMonospaceBearings = (flags & 2) == 0;
        if ((flags & 0xFC) != 0 || (hasProportionalBearings && hasMonospaceBearings) || metricsCount < 1 || metricsCount > glyphCount)
            return null;

        var expected = 1 + (metricsCount * 2) + (hasProportionalBearings ? metricsCount * 2 : 0) + (hasMonospaceBearings ? (glyphCount - metricsCount) * 2 : 0);
        if (source.Length < expected)
            return null;

        var output = new byte[(metricsCount * 4) + ((glyphCount - metricsCount) * 2)];
        var position = 1 + (metricsCount * 2);
        for (var i = 0; i < metricsCount; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(i * 4), FontBinary.U16(source, 1 + (i * 2)));
            short bearing;
            if (hasProportionalBearings)
            {
                bearing = FontBinary.I16(source, position);
                position += 2;
            }
            else
            {
                bearing = xMins[i];
            }

            BinaryPrimitives.WriteInt16BigEndian(output.AsSpan((i * 4) + 2), bearing);
        }

        for (var i = metricsCount; i < glyphCount; i++)
        {
            short bearing;
            if (hasMonospaceBearings)
            {
                bearing = FontBinary.I16(source, position);
                position += 2;
            }
            else
            {
                bearing = xMins[i];
            }

            BinaryPrimitives.WriteInt16BigEndian(output.AsSpan((metricsCount * 4) + ((i - metricsCount) * 2)), bearing);
        }

        return output;
    }

    /// <summary>Reads a <c>UIntBase128</c> (https://www.w3.org/TR/WOFF2/#DataTypes).</summary>
    internal static bool ReadUIntBase128(ReadOnlySpan<byte> data, ref int position, out uint value)
    {
        value = 0;
        for (var i = 0; i < 5; i++)
        {
            if (position >= data.Length)
                return false;

            var b = data[position++];

            // No leading zeros, and no overflow
            if ((i == 0 && b == 0x80) || (value & 0xFE000000) != 0)
                return false;

            value = (value << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0)
                return true;
        }

        return false;
    }

    /// <summary>Reads a <c>255UInt16</c> (https://www.w3.org/TR/WOFF2/#DataTypes).</summary>
    internal static bool Read255UInt16(ReadOnlySpan<byte> data, ref int position, out int value)
    {
        const byte WordCode = 253, OneMoreByteCode2 = 254, OneMoreByteCode1 = 255, LowestUCode = 253;
        value = 0;
        if (position >= data.Length)
            return false;

        var code = data[position++];
        switch (code)
        {
            case WordCode:
                if (position + 2 > data.Length)
                    return false;

                value = FontBinary.U16(data, position);
                position += 2;
                return true;

            case OneMoreByteCode1:
                if (position >= data.Length)
                    return false;

                value = data[position++] + LowestUCode;
                return true;

            case OneMoreByteCode2:
                if (position >= data.Length)
                    return false;

                value = data[position++] + (LowestUCode * 2);
                return true;

            default:
                value = code;
                return true;
        }
    }

    /// <summary>The growing output of the reconstructed glyf table.</summary>
    private struct GlyfWriter(int capacity)
    {
        private byte[] _buffer = new byte[capacity];

        public int Length { get; private set; }

        public void WriteU16(ushort value)
        {
            var span = Allocate(2);
            BinaryPrimitives.WriteUInt16BigEndian(span, value);
        }

        public void WriteI16(short value) => WriteU16((ushort)value);

        public void Write(ReadOnlySpan<byte> data) => data.CopyTo(Allocate(data.Length));

        public void Skip(int count) => Allocate(count).Clear();

        public readonly void Patch(int offset, ReadOnlySpan<byte> data) => data.CopyTo(_buffer.AsSpan(offset));

        public void Pad4() => Skip(((Length + 3) & ~3) - Length);

        /// <summary>Writes the flags and the coordinates of the points of a simple glyph (x, y, on curve triplets), as the reference decoder does.</summary>
        public void WritePoints(ReadOnlySpan<int> points, bool overlap)
        {
            const byte OnCurve = 0x01, XShort = 0x02, YShort = 0x04, Repeat = 0x08, XIsSame = 0x10, YIsSame = 0x20, OverlapSimple = 0x40;
            var pointCount = points.Length / 3;

            // The flags, with the repetitions
            var lastFlag = -1;
            var repeatCount = 0;
            int lastX = 0, lastY = 0, xLength = 0, yLength = 0;
            for (var i = 0; i < pointCount; i++)
            {
                var flag = points[(i * 3) + 2] != 0 ? OnCurve : 0;
                if (overlap && i == 0)
                {
                    flag |= OverlapSimple;
                }

                var dx = points[i * 3] - lastX;
                var dy = points[(i * 3) + 1] - lastY;
                if (dx == 0)
                {
                    flag |= XIsSame;
                }
                else if (dx is > -256 and < 256)
                {
                    flag |= XShort | (dx > 0 ? XIsSame : 0);
                    xLength++;
                }
                else
                {
                    xLength += 2;
                }

                if (dy == 0)
                {
                    flag |= YIsSame;
                }
                else if (dy is > -256 and < 256)
                {
                    flag |= YShort | (dy > 0 ? YIsSame : 0);
                    yLength++;
                }
                else
                {
                    yLength += 2;
                }

                if (flag == lastFlag && repeatCount != 255)
                {
                    _buffer[Length - 1] |= Repeat;
                    repeatCount++;
                }
                else
                {
                    if (repeatCount != 0)
                    {
                        Allocate(1)[0] = (byte)repeatCount;
                    }

                    Allocate(1)[0] = (byte)flag;
                    repeatCount = 0;
                }

                lastX = points[i * 3];
                lastY = points[(i * 3) + 1];
                lastFlag = flag;
            }

            if (repeatCount != 0)
            {
                Allocate(1)[0] = (byte)repeatCount;
            }

            // The coordinates, relative to the previous point
            var coordinates = Allocate(xLength + yLength);
            var position = 0;
            for (var axis = 0; axis < 2; axis++)
            {
                var last = 0;
                for (var i = 0; i < pointCount; i++)
                {
                    var value = points[(i * 3) + axis];
                    var delta = value - last;
                    last = value;
                    if (delta == 0)
                        continue;

                    if (delta is > -256 and < 256)
                    {
                        coordinates[position++] = (byte)Math.Abs(delta);
                    }
                    else
                    {
                        BinaryPrimitives.WriteInt16BigEndian(coordinates[position..], (short)delta);
                        position += 2;
                    }
                }
            }
        }

        public readonly byte[] ToArray() => _buffer.AsSpan(0, Length).ToArray();

        private Span<byte> Allocate(int count)
        {
            if (Length + count > _buffer.Length)
            {
                Array.Resize(ref _buffer, Math.Max(Length + count, _buffer.Length * 2));
            }

            var span = _buffer.AsSpan(Length, count);
            Length += count;
            return span;
        }
    }
}
