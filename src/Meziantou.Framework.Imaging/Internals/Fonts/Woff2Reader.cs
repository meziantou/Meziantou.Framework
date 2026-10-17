using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Expands a WOFF 2.0 file to the font or the font collection it wraps.</summary>
/// <remarks>
/// <para>
/// W3C Recommendation "WOFF File Format 2.0". The tables of the font are one Brotli stream; the glyph outlines, the
/// glyph locations and the horizontal metrics can be stored in a transformed form that this class reverses
/// (<see cref="Woff2GlyfTransform"/>, sections 5.1 to 5.4 of the specification). The other tables are copied.
/// </para>
/// <para>
/// The rebuilt font is not byte-identical to the one the file was made from: the glyphs are written again, with the
/// same points. The bytes of the Brotli stream and the rebuilt glyph table are temporary storage, bounded by
/// <see cref="ImageResourceLimits.MaxLiveAllocationBytes"/>; the font itself is bounded by
/// <see cref="ImageResourceLimits.MaxFontBytes"/> before it is allocated.
/// </para>
/// </remarks>
internal static class Woff2Reader
{
    private const int HeaderLength = 48;
    private const int ArbitraryTag = 63;

    // Section 4.1, "Known Table Tags": the tags a table directory entry stores as an index.
    private static ReadOnlySpan<byte> KnownTags =>
        "cmapheadhheahmtxmaxpnameOS/2postcvt fpgmglyflocaprepCFF VORGEBDTEBLCgasphdmxkernLTSHPCLTVDMXvheavmtxBASEGDEFGPOSGSUBEBSCJSTFMATHCBDTCBLCCOLRCPALSVG sbixacntavarbdatblocbslncvarfdscfeatfmtxfvargvarhstyjustlcarmortmorxopbdproptrakZapfSilfGlatGlocFeatSill"u8;

    public static byte[] Decode(byte[] file, ImageConfiguration configuration)
    {
        var limits = configuration.Limits;
        ReadOnlySpan<byte> data = file;
        var header = FontBinary.Slice(data, 0, HeaderLength);
        var flavor = FontBinary.UInt32(header, 4);
        var tableCount = FontBinary.UInt16(header, 12);
        var compressedLength = FontBinary.UInt32(header, 20);
        if (tableCount == 0)
            throw FontErrors.Invalid("the WOFF2 file has no table.");

        var cursor = HeaderLength;
        var tables = ReadTableDirectory(data, ref cursor, tableCount, limits, out var sourceLength);
        uint collectionVersion = 0;
        var faces = flavor == FontTags.Collection ? ReadCollectionDirectory(data, ref cursor, tableCount, out collectionVersion) : [new Face(flavor, CreateSequence(tableCount))];
        AssignTables(tables, faces);

        var compressed = FontBinary.Slice(data, cursor, compressedLength);
        var scope = AllocationScope.Create(configuration, "WOFF2");
        if (sourceLength > CheckedSizes.MaxBufferLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.LiveAllocationBytes, scope.Limit, sourceLength);

        using var source = scope.Rent((int)sourceLength, AllocationKind.Temporary, clear: false);
        Decompress(compressed, source.Span);

        var rebuilt = new FontByteWriter?[tableCount];
        try
        {
            return Build(source.Span, tables, faces, flavor, collectionVersion, rebuilt, scope, limits);
        }
        finally
        {
            foreach (var writer in rebuilt)
            {
                writer?.Dispose();
            }
        }
    }

    /// <summary>Reads the table directory: section 4.1.</summary>
    private static Table[] ReadTableDirectory(ReadOnlySpan<byte> data, ref int cursor, int tableCount, ImageResourceLimits limits, out long sourceLength)
    {
        var tables = new Table[tableCount];
        sourceLength = 0;
        long copiedLength = 0;
        for (var i = 0; i < tableCount; i++)
        {
            var flags = FontBinary.UInt8(data, cursor++);
            uint tag;
            if ((flags & 0x3F) == ArbitraryTag)
            {
                tag = FontBinary.UInt32(data, cursor);
                cursor += 4;
            }
            else
            {
                tag = BinaryPrimitives.ReadUInt32BigEndian(KnownTags[((flags & 0x3F) * 4)..]);
            }

            // The transformation version: 0 is the transformed form of glyf and loca and the plain form of every other
            // table; 3 is the plain form of glyf and loca; 1 is the transformed form of hmtx.
            var version = flags >> 6;
            var transformed = tag switch
            {
                FontTags.Glyf or FontTags.Loca when version is 0 or 3 => version == 0,
                FontTags.Hmtx when version is 0 or 1 => version == 1,
                not (FontTags.Glyf or FontTags.Loca or FontTags.Hmtx) when version == 0 => false,
                _ => throw FontErrors.InvalidTable(tag, $"uses the unknown WOFF2 transformation {version}."),
            };

            var originalLength = ReadBase128(data, ref cursor);
            var length = transformed ? ReadBase128(data, ref cursor) : originalLength;
            if (transformed && tag == FontTags.Loca && length != 0)
                throw FontErrors.InvalidTable(tag, "has data in its transformed form, which is always empty.");

            tables[i] = new Table { Tag = tag, IsTransformed = transformed, SourceOffset = sourceLength, SourceLength = length, Owner = -1, Partner = -1 };
            sourceLength += length;
            if (!transformed)
            {
                copiedLength += length;
            }
        }

        // The tables that are copied are part of the font: more of them than a font can be is refused before the stream
        // is expanded.
        if (copiedLength > limits.MaxFontBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FontBytes, limits.MaxFontBytes, copiedLength);

        return tables;
    }

    /// <summary>Reads the collection directory: section 4.2.</summary>
    private static Face[] ReadCollectionDirectory(ReadOnlySpan<byte> data, ref int cursor, int tableCount, out uint version)
    {
        version = FontBinary.UInt32(data, cursor);
        cursor += 4;
        if (version is not (0x00010000 or 0x00020000))
            throw FontErrors.Invalid($"the font collection has the unknown version 0x{version:X8}.");

        var faceCount = Read255UInt16(data, ref cursor);
        if (faceCount == 0)
            throw FontErrors.Invalid("the font collection has no font.");

        var faces = new Face[faceCount];
        for (var i = 0; i < faceCount; i++)
        {
            var count = Read255UInt16(data, ref cursor);
            var flavor = FontBinary.UInt32(data, cursor);
            cursor += 4;

            // Each index is at least one byte of the file: the array cannot be larger than the file allows.
            FontBinary.EnsureRange(data, cursor, count);
            var indices = new int[count];
            for (var j = 0; j < count; j++)
            {
                indices[j] = Read255UInt16(data, ref cursor);
                if (indices[j] >= tableCount)
                    throw FontErrors.Invalid("a font of the collection refers to a table that does not exist.");
            }

            faces[i] = new Face(flavor, indices);
        }

        return faces;
    }

    private static int[] CreateSequence(int count)
    {
        var result = new int[count];
        for (var i = 0; i < count; i++)
        {
            result[i] = i;
        }

        return result;
    }

    /// <summary>Finds the tables each face needs to reverse the transformations, and pairs each glyph table with its location table.</summary>
    private static void AssignTables(Table[] tables, Face[] faces)
    {
        for (var faceIndex = 0; faceIndex < faces.Length; faceIndex++)
        {
            var face = faces[faceIndex];
            foreach (var index in face.Tables)
            {
                ref var table = ref tables[index];
                if (table.Owner < 0)
                {
                    table.Owner = faceIndex;
                }

                ref var slot = ref face.GetSlot(table.Tag, out var known);
                if (!known)
                    continue;

                if (slot >= 0)
                    throw FontErrors.InvalidTable(table.Tag, "is listed twice.");

                slot = index;
            }

            var hasTransformedGlyf = face.Glyf >= 0 && tables[face.Glyf].IsTransformed;
            var hasTransformedLoca = face.Loca >= 0 && tables[face.Loca].IsTransformed;
            if (hasTransformedGlyf != hasTransformedLoca)
                throw FontErrors.Invalid("the glyph table and the glyph location table of the WOFF2 file are not transformed together.");

            if (hasTransformedGlyf)
            {
                // Two faces that share one of the two tables share the other one.
                ref var glyf = ref tables[face.Glyf];
                ref var loca = ref tables[face.Loca];
                if ((glyf.Partner >= 0 && glyf.Partner != face.Loca) || (loca.Partner >= 0 && loca.Partner != face.Glyf))
                    throw FontErrors.Invalid("a glyph table of the WOFF2 file is used with two glyph location tables.");

                glyf.Partner = face.Loca;
                loca.Partner = face.Glyf;
            }
        }
    }

    /// <summary>Expands the Brotli stream of the tables, whose expanded length the table directory gives.</summary>
    private static void Decompress(ReadOnlySpan<byte> compressed, Span<byte> destination)
    {
        using var decoder = new BrotliDecoder();
        var status = decoder.Decompress(compressed, destination, out var consumed, out var written);
        if (status == OperationStatus.DestinationTooSmall && written == destination.Length)
        {
            // The destination is full: the stream is valid when it ends here, without one more byte.
            Span<byte> extra = stackalloc byte[1];
            status = decoder.Decompress(compressed[consumed..], extra, out _, out var extraWritten);
            if (extraWritten != 0)
                throw FontErrors.Invalid("the compressed data of the WOFF2 file is longer than its tables.");
        }

        if (status != OperationStatus.Done || written != destination.Length)
        {
            throw FontErrors.Invalid(status switch
            {
                OperationStatus.NeedMoreData => "the compressed data of the WOFF2 file is truncated.",
                OperationStatus.Done => "the compressed data of the WOFF2 file is shorter than its tables.",
                OperationStatus.DestinationTooSmall => "the compressed data of the WOFF2 file is longer than its tables.",
                _ => "the compressed data of the WOFF2 file is malformed.",
            });
        }
    }

    private static byte[] Build(ReadOnlySpan<byte> source, Table[] tables, Face[] faces, uint flavor, uint collectionVersion, FontByteWriter?[] rebuilt, AllocationScope scope, ImageResourceLimits limits)
    {
        // The length of each table of the font. The glyph tables are rebuilt first: their length is not known otherwise.
        for (var i = 0; i < tables.Length; i++)
        {
            ref var table = ref tables[i];
            if (table.Owner < 0)
                continue;

            if (!table.IsTransformed)
            {
                table.OutputLength = table.SourceLength;
            }
            else if (table.Tag == FontTags.Glyf)
            {
                var glyf = rebuilt[i] = new FontByteWriter(scope);
                var loca = rebuilt[table.Partner] = new FontByteWriter(scope);
                Woff2GlyfTransform.Rebuild(FontBinary.Slice(source, table.SourceOffset, table.SourceLength), glyf, loca, out var glyphCount, out var hasLongOffsets);
                table.OutputLength = glyf.Length;
                table.GlyphCount = glyphCount;
                table.HasLongOffsets = hasLongOffsets;
                tables[table.Partner].OutputLength = loca.Length;
            }
            else if (table.Tag == FontTags.Hmtx)
            {
                var face = faces[table.Owner];
                if (face.Hhea < 0 || face.Maxp < 0 || face.Glyf < 0 || face.Loca < 0 || face.Head < 0)
                    throw FontErrors.InvalidTable(table.Tag, "is transformed in a font that does not have the tables its transformation needs.");

                table.GlyphCount = FontBinary.UInt16(GetSource(source, tables[face.Maxp]), 4);
                table.MetricCount = FontBinary.UInt16(GetSource(source, tables[face.Hhea]), 34);
                if (table.MetricCount == 0 || table.MetricCount > table.GlyphCount)
                    throw FontErrors.InvalidTable(FontTags.Hhea, $"declares {table.MetricCount} horizontal metrics for {table.GlyphCount} glyphs.");

                table.OutputLength = ((long)table.MetricCount * 4) + ((long)(table.GlyphCount - table.MetricCount) * 2);
            }
        }

        // The layout of the font: the header of the collection, the directory of each face, then the tables.
        long position = 0;
        if (flavor == FontTags.Collection)
        {
            position = 12 + ((long)faces.Length * 4) + (collectionVersion == 0x00020000 ? 12 : 0);
        }

        foreach (var face in faces)
        {
            face.DirectoryOffset = position;
            position += SfntWriter.GetDirectoryLength(face.Tables.Length);
        }

        for (var i = 0; i < tables.Length; i++)
        {
            if (tables[i].Owner < 0)
                continue;

            tables[i].OutputOffset = position;
            position += SfntWriter.Pad(tables[i].OutputLength);
        }

        if (position > limits.MaxFontBytes)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FontBytes, limits.MaxFontBytes, position);

        if (position > CheckedSizes.MaxBufferLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.FontBytes, CheckedSizes.MaxBufferLength, position);

        var font = new byte[position];
        if (flavor == FontTags.Collection)
        {
            // OpenType specification, "Font Collections": the tag, the version, the number of fonts and their offsets.
            // A version 2 header ends with the three fields of a digital signature, left to zero: there is none.
            BinaryPrimitives.WriteUInt32BigEndian(font, FontTags.Collection);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(4), collectionVersion);
            BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(8), (uint)faces.Length);
            for (var i = 0; i < faces.Length; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(12 + (i * 4)), (uint)faces[i].DirectoryOffset);
            }
        }

        foreach (var face in faces)
        {
            var directory = font.AsSpan((int)face.DirectoryOffset);
            SfntWriter.WriteHeader(directory, face.Flavor, face.Tables.Length);
            for (var i = 0; i < face.Tables.Length; i++)
            {
                ref readonly var table = ref tables[face.Tables[i]];
                SfntWriter.WriteRecord(directory[(SfntWriter.HeaderLength + (i * SfntWriter.RecordLength))..], table.Tag, checksum: 0, table.OutputOffset, table.OutputLength);
            }
        }

        // The tables that are copied and the glyph tables, then the metrics that are computed from the glyph tables.
        for (var i = 0; i < tables.Length; i++)
        {
            ref readonly var table = ref tables[i];
            if (table.Owner < 0)
                continue;

            var destination = font.AsSpan((int)table.OutputOffset, (int)table.OutputLength);
            if (rebuilt[i] is { } writer)
            {
                writer.Written.CopyTo(destination);
            }
            else if (!table.IsTransformed)
            {
                GetSource(source, table).CopyTo(destination);
            }
        }

        for (var i = 0; i < tables.Length; i++)
        {
            ref readonly var table = ref tables[i];
            if (table.Owner < 0 || !table.IsTransformed || table.Tag != FontTags.Hmtx)
                continue;

            var face = faces[table.Owner];
            ref readonly var glyf = ref tables[face.Glyf];
            ref readonly var loca = ref tables[face.Loca];
            var hasLongOffsets = glyf.IsTransformed ? glyf.HasLongOffsets : FontBinary.Int16(GetSource(source, tables[face.Head]), 50) != 0;
            Woff2GlyfTransform.RebuildHorizontalMetrics(
                GetSource(source, table),
                font.AsSpan((int)table.OutputOffset, (int)table.OutputLength),
                table.GlyphCount,
                table.MetricCount,
                font.AsSpan((int)glyf.OutputOffset, (int)glyf.OutputLength),
                font.AsSpan((int)loca.OutputOffset, (int)loca.OutputLength),
                hasLongOffsets);
        }

        // The header of a font says how its glyph locations are stored: it must agree with the rebuilt table.
        foreach (var face in faces)
        {
            if (face.Glyf < 0 || face.Head < 0 || !tables[face.Glyf].IsTransformed)
                continue;

            var declaresLongOffsets = FontBinary.Int16(GetSource(source, tables[face.Head]), 50) != 0;
            if (declaresLongOffsets != tables[face.Glyf].HasLongOffsets)
                throw FontErrors.InvalidTable(FontTags.Head, "and the transformed glyph table of the WOFF2 file disagree on the format of the glyph locations.");
        }

        return font;
    }

    private static ReadOnlySpan<byte> GetSource(ReadOnlySpan<byte> source, in Table table) => FontBinary.Slice(source, table.SourceOffset, table.SourceLength);

    /// <summary>Reads a UIntBase128 value: section 3.1. Seven bits per byte, most significant first, at most five bytes.</summary>
    private static long ReadBase128(ReadOnlySpan<byte> data, ref int cursor)
    {
        uint value = 0;
        for (var i = 0; i < 5; i++)
        {
            var current = FontBinary.UInt8(data, cursor++);

            // A value has one encoding: no leading zero, and no bit beyond the thirty-second.
            if ((i == 0 && current == 0x80) || (value & 0xFE000000) != 0)
                break;

            value = (value << 7) | (uint)(current & 0x7F);
            if ((current & 0x80) == 0)
                return value;
        }

        throw FontErrors.Invalid("the table directory of the WOFF2 file has a malformed length.");
    }

    /// <summary>Reads a 255UInt16 value: section 3.2. One byte for the small values, three codes for the others.</summary>
    internal static int Read255UInt16(ReadOnlySpan<byte> data, ref int cursor)
    {
        var code = FontBinary.UInt8(data, cursor++);
        switch (code)
        {
            case 253:
                var word = FontBinary.UInt16(data, cursor);
                cursor += 2;
                return word;

            case 254:
                return FontBinary.UInt8(data, cursor++) + 506;

            case 255:
                return FontBinary.UInt8(data, cursor++) + 253;

            default:
                return code;
        }
    }

    [StructLayout(LayoutKind.Auto)]
    private struct Table
    {
        public uint Tag;
        public bool IsTransformed;

        /// <summary>Where the stored form of the table is in the expanded stream.</summary>
        public long SourceOffset;
        public long SourceLength;

        /// <summary>Where the table is in the font.</summary>
        public long OutputOffset;
        public long OutputLength;

        /// <summary>The first face that has the table, or -1 when no face has it: the table is then left out.</summary>
        public int Owner;

        /// <summary>The location table of a transformed glyph table, and the reverse.</summary>
        public int Partner;
        public int GlyphCount;
        public int MetricCount;
        public bool HasLongOffsets;
    }

    private sealed class Face(uint flavor, int[] tables)
    {
        private int _glyf = -1;
        private int _loca = -1;
        private int _head = -1;
        private int _hhea = -1;
        private int _maxp = -1;
        private int _hmtx = -1;
        private int _other;

        public uint Flavor { get; } = flavor;

        /// <summary>Gets the tables of the face, as indices in the table directory of the file.</summary>
        public int[] Tables { get; } = tables;

        public long DirectoryOffset { get; set; }

        public int Glyf => _glyf;

        public int Loca => _loca;

        public int Head => _head;

        public int Hhea => _hhea;

        public int Maxp => _maxp;

        /// <summary>Gets where the face keeps the index of one of the tables the transformations need.</summary>
        public ref int GetSlot(uint tag, out bool known)
        {
            known = true;
            switch (tag)
            {
                case FontTags.Glyf:
                    return ref _glyf;

                case FontTags.Loca:
                    return ref _loca;

                case FontTags.Head:
                    return ref _head;

                case FontTags.Hhea:
                    return ref _hhea;

                case FontTags.Maxp:
                    return ref _maxp;

                case FontTags.Hmtx:
                    return ref _hmtx;

                default:
                    known = false;
                    return ref _other;
            }
        }
    }
}
