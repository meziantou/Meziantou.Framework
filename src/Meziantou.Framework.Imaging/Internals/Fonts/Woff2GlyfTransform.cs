using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Reverses the transformations of the glyph tables of a WOFF 2.0 file: W3C Recommendation "WOFF File Format 2.0",
/// sections 5.1 (glyf), 5.3 (loca) and 5.4 (hmtx).
/// </summary>
/// <remarks>
/// The transformed glyph table stores each kind of value of the glyphs in its own stream. The glyphs are written back
/// in the format of the OpenType specification, each padded to four bytes; the points keep their values, the way they
/// are encoded is not the one of the original font.
/// </remarks>
internal static class Woff2GlyfTransform
{
    private const int HeaderLength = 36;
    private const int GlyphHeaderLength = 10;

    // Flags of the points of a glyph, OpenType specification, "glyf - Glyph Data".
    private const int OnCurve = 0x01;
    private const int XShortVector = 0x02;
    private const int YShortVector = 0x04;
    private const int XIsSameOrPositive = 0x10;
    private const int YIsSameOrPositive = 0x20;
    private const int OverlapSimple = 0x40;

    // Flags of the components of a composite glyph.
    private const int ArgumentsAreWords = 0x0001;
    private const int HasScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HasXAndYScale = 0x0040;
    private const int HasTwoByTwo = 0x0080;
    private const int HasInstructions = 0x0100;

    /// <summary>Rebuilds a glyph table and its location table.</summary>
    public static void Rebuild(ReadOnlySpan<byte> table, FontByteWriter glyf, FontByteWriter loca, out int glyphCount, out bool hasLongOffsets)
    {
        var header = FontBinary.Slice(table, 0, HeaderLength);
        var options = FontBinary.UInt16(header, 2);
        glyphCount = FontBinary.UInt16(header, 4);
        var indexFormat = FontBinary.UInt16(header, 6);
        if (indexFormat > 1)
            throw FontErrors.InvalidTable(FontTags.Glyf, $"declares the unknown location format {indexFormat} in its transformed form.");

        hasLongOffsets = indexFormat == 1;

        // The seven streams follow the header, in the order of their lengths.
        long offset = HeaderLength;
        var contourCounts = Take(table, ref offset, FontBinary.UInt32(header, 8));
        var pointCounts = Take(table, ref offset, FontBinary.UInt32(header, 12));
        var flags = Take(table, ref offset, FontBinary.UInt32(header, 16));
        var glyphs = Take(table, ref offset, FontBinary.UInt32(header, 20));
        var composites = Take(table, ref offset, FontBinary.UInt32(header, 24));
        var boxes = Take(table, ref offset, FontBinary.UInt32(header, 28));
        var instructions = Take(table, ref offset, FontBinary.UInt32(header, 32));
        var overlaps = (options & 1) != 0 ? FontBinary.Slice(table, offset, (glyphCount + 7) >> 3) : default;

        // One bit per glyph says whether its bounding box is stored; the boxes follow the bits, padded to four bytes.
        var boxBits = FontBinary.Slice(boxes, 0, ((glyphCount + 31) >> 5) * 4);
        FontBinary.EnsureRange(contourCounts, 0, (long)glyphCount * 2);

        var state = new Cursors { Box = boxBits.Length };
        var locations = loca.Append(((long)glyphCount + 1) * (hasLongOffsets ? 4 : 2));
        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            WriteLocation(locations, glyph, glyf.Length, hasLongOffsets);
            var contourCount = BinaryPrimitives.ReadInt16BigEndian(contourCounts[(glyph * 2)..]);
            var hasBox = (boxBits[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;
            var overlaps1 = !overlaps.IsEmpty && (overlaps[glyph >> 3] & (0x80 >> (glyph & 7))) != 0;
            switch (contourCount)
            {
                case 0:
                    if (hasBox)
                        throw FontErrors.InvalidTable(FontTags.Glyf, "stores a bounding box for a glyph without outline.");

                    break;

                case > 0:
                    WriteSimpleGlyph(glyf, contourCount, hasBox, overlaps1, pointCounts, flags, glyphs, boxes, instructions, ref state);
                    break;

                case -1:
                    if (!hasBox)
                        throw FontErrors.InvalidTable(FontTags.Glyf, "does not store the bounding box of a composite glyph.");

                    WriteCompositeGlyph(glyf, glyphs, composites, boxes, instructions, ref state);
                    break;

                default:
                    throw FontErrors.InvalidTable(FontTags.Glyf, $"declares a glyph with {contourCount} contours.");
            }
        }

        WriteLocation(locations, glyphCount, glyf.Length, hasLongOffsets);
    }

    /// <summary>Rebuilds a horizontal metrics table whose left side bearings were removed: they are the minimum x of the glyphs.</summary>
    public static void RebuildHorizontalMetrics(ReadOnlySpan<byte> table, Span<byte> destination, int glyphCount, int metricCount, ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, bool hasLongOffsets)
    {
        // Bit 0: the bearings of the glyphs that have an advance are omitted. Bit 1: the bearings of the other glyphs are.
        var options = FontBinary.UInt8(table, 0);
        if ((options & 0xFC) != 0 || (options & 0x03) == 0)
            throw FontErrors.InvalidTable(FontTags.Hmtx, $"has the invalid flags 0x{options:X2} in its transformed form.");

        var hasProportionalBearings = (options & 0x01) == 0;
        var hasMonospacedBearings = (options & 0x02) == 0;
        var advances = FontBinary.Slice(table, 1, (long)metricCount * 2);
        long offset = 1 + advances.Length;
        var proportionalBearings = hasProportionalBearings ? Take(table, ref offset, (uint)metricCount * 2) : default;
        var monospacedBearings = hasMonospacedBearings ? Take(table, ref offset, (uint)(glyphCount - metricCount) * 2) : default;

        for (var glyph = 0; glyph < glyphCount; glyph++)
        {
            Span<byte> bearing;
            ReadOnlySpan<byte> stored;
            if (glyph < metricCount)
            {
                advances.Slice(glyph * 2, 2).CopyTo(destination[(glyph * 4)..]);
                bearing = destination.Slice((glyph * 4) + 2, 2);
                stored = hasProportionalBearings ? proportionalBearings.Slice(glyph * 2, 2) : default;
            }
            else
            {
                bearing = destination.Slice((metricCount * 4) + ((glyph - metricCount) * 2), 2);
                stored = hasMonospacedBearings ? monospacedBearings.Slice((glyph - metricCount) * 2, 2) : default;
            }

            if (stored.IsEmpty)
            {
                BinaryPrimitives.WriteInt16BigEndian(bearing, (short)GetMinimumX(glyf, loca, hasLongOffsets, glyph));
            }
            else
            {
                stored.CopyTo(bearing);
            }
        }
    }

    /// <summary>Gets the minimum x the header of a glyph declares, or 0 for a glyph without outline.</summary>
    private static int GetMinimumX(ReadOnlySpan<byte> glyf, ReadOnlySpan<byte> loca, bool hasLongOffsets, int glyph)
    {
        long start;
        long end;
        if (hasLongOffsets)
        {
            start = FontBinary.UInt32(loca, glyph * 4);
            end = FontBinary.UInt32(loca, (glyph * 4) + 4);
        }
        else
        {
            start = FontBinary.UInt16(loca, glyph * 2) * 2L;
            end = FontBinary.UInt16(loca, (glyph * 2) + 2) * 2L;
        }

        if (end <= start)
            return 0;

        return FontBinary.Int16(FontBinary.Slice(glyf, start, end - start), 2);
    }

    private static void WriteSimpleGlyph(
        FontByteWriter glyf,
        int contourCount,
        bool hasBox,
        bool overlaps,
        ReadOnlySpan<byte> pointCounts,
        ReadOnlySpan<byte> flags,
        ReadOnlySpan<byte> glyphs,
        ReadOnlySpan<byte> boxes,
        ReadOnlySpan<byte> instructions,
        ref Cursors state)
    {
        // The number of points, to size the glyph: the counts of the contours are read again when they are written.
        var pointCount = 0;
        var cursor = state.PointCount;
        for (var i = 0; i < contourCount; i++)
        {
            var count = Woff2Reader.Read255UInt16(pointCounts, ref cursor);
            if (count == 0)
                throw FontErrors.InvalidTable(FontTags.Glyf, "declares a contour without point.");

            pointCount += count;
            if (pointCount > 65536)
                throw FontErrors.InvalidTable(FontTags.Glyf, "declares a glyph with more points than a glyph can have.");
        }

        // The coordinates of the points come first in the glyph stream, then the length of the instructions.
        var pointFlags = FontBinary.Slice(flags, state.Flag, pointCount);
        var coordinatesLength = 0;
        foreach (var flag in pointFlags)
        {
            coordinatesLength += GetCoordinatesLength(flag);
        }

        var coordinates = FontBinary.Slice(glyphs, state.Glyph, coordinatesLength);
        state.Flag += pointCount;
        state.Glyph += coordinatesLength;
        var instructionLength = Woff2Reader.Read255UInt16(glyphs, ref state.Glyph);
        var glyphInstructions = FontBinary.Slice(instructions, state.Instruction, instructionLength);
        state.Instruction += instructionLength;

        // The largest glyph these values can make: a flag and two 16-bit coordinates per point.
        var start = glyf.Length;
        var record = glyf.Append(GlyphHeaderLength + (contourCount * 2) + 2 + instructionLength + (pointCount * 5L) + 3);
        BinaryPrimitives.WriteInt16BigEndian(record, (short)contourCount);
        var position = GlyphHeaderLength;
        var lastPoint = -1;
        for (var i = 0; i < contourCount; i++)
        {
            lastPoint += Woff2Reader.Read255UInt16(pointCounts, ref state.PointCount);
            BinaryPrimitives.WriteUInt16BigEndian(record[position..], (ushort)lastPoint);
            position += 2;
        }

        BinaryPrimitives.WriteUInt16BigEndian(record[position..], (ushort)instructionLength);
        position += 2;
        glyphInstructions.CopyTo(record[position..]);
        position += instructionLength;

        // The flags, then the x coordinates, then the y coordinates. The y coordinates are written after the largest
        // size the x coordinates can have, and moved once that size is known.
        var outputFlags = record.Slice(position, pointCount);
        var outputX = record.Slice(position + pointCount, pointCount * 2);
        var outputY = record.Slice(position + (pointCount * 3), pointCount * 2);
        var lengthX = 0;
        var lengthY = 0;
        var x = 0;
        var y = 0;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var coordinate = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var flag = pointFlags[i];
            var length = GetCoordinatesLength(flag);
            DecodeCoordinates(flag, coordinates.Slice(coordinate, length), out var dx, out var dy);
            coordinate += length;
            x += dx;
            y += dy;
            if (x is < short.MinValue or > short.MaxValue || y is < short.MinValue or > short.MaxValue)
                throw FontErrors.InvalidTable(FontTags.Glyf, "has a point outside the coordinates a glyph can have.");

            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);

            // The high bit of a flag of the transformed table is set for a control point.
            var outputFlag = (flag & 0x80) == 0 ? OnCurve : 0;
            outputFlag |= WriteDelta(dx, outputX, ref lengthX, XShortVector, XIsSameOrPositive);
            outputFlag |= WriteDelta(dy, outputY, ref lengthY, YShortVector, YIsSameOrPositive);
            outputFlags[i] = (byte)outputFlag;
        }

        if (overlaps)
        {
            outputFlags[0] |= OverlapSimple;
        }

        position += pointCount + lengthX;
        outputY[..lengthY].CopyTo(record[position..]);
        position += lengthY;

        var box = record.Slice(2, 8);
        if (hasBox)
        {
            FontBinary.Slice(boxes, state.Box, 8).CopyTo(box);
            state.Box += 8;
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(box, (short)minX);
            BinaryPrimitives.WriteInt16BigEndian(box[2..], (short)minY);
            BinaryPrimitives.WriteInt16BigEndian(box[4..], (short)maxX);
            BinaryPrimitives.WriteInt16BigEndian(box[6..], (short)maxY);
        }

        Finish(glyf, record, start, position);
    }

    private static void WriteCompositeGlyph(FontByteWriter glyf, ReadOnlySpan<byte> glyphs, ReadOnlySpan<byte> composites, ReadOnlySpan<byte> boxes, ReadOnlySpan<byte> instructions, ref Cursors state)
    {
        // The components are stored as in the glyph table: their length depends on the flags of each one.
        var componentsStart = state.Composite;
        var hasInstructions = false;
        int flags;
        do
        {
            flags = FontBinary.UInt16(composites, state.Composite);
            hasInstructions |= (flags & HasInstructions) != 0;
            var length = 4 + ((flags & ArgumentsAreWords) != 0 ? 4 : 2);
            if ((flags & HasScale) != 0)
            {
                length += 2;
            }
            else if ((flags & HasXAndYScale) != 0)
            {
                length += 4;
            }
            else if ((flags & HasTwoByTwo) != 0)
            {
                length += 8;
            }

            state.Composite += length;
        }
        while ((flags & MoreComponents) != 0);

        var components = FontBinary.Slice(composites, componentsStart, state.Composite - componentsStart);
        var instructionLength = hasInstructions ? Woff2Reader.Read255UInt16(glyphs, ref state.Glyph) : 0;
        var glyphInstructions = FontBinary.Slice(instructions, state.Instruction, instructionLength);
        state.Instruction += instructionLength;

        var start = glyf.Length;
        var record = glyf.Append(GlyphHeaderLength + components.Length + (hasInstructions ? 2 + instructionLength : 0) + 3L);
        BinaryPrimitives.WriteInt16BigEndian(record, -1);
        FontBinary.Slice(boxes, state.Box, 8).CopyTo(record[2..]);
        state.Box += 8;
        var position = GlyphHeaderLength;
        components.CopyTo(record[position..]);
        position += components.Length;
        if (hasInstructions)
        {
            BinaryPrimitives.WriteUInt16BigEndian(record[position..], (ushort)instructionLength);
            position += 2;
            glyphInstructions.CopyTo(record[position..]);
            position += instructionLength;
        }

        Finish(glyf, record, start, position);
    }

    /// <summary>Ends a glyph at a multiple of four bytes, which every format of the location table can address.</summary>
    private static void Finish(FontByteWriter glyf, Span<byte> record, int start, int length)
    {
        var padded = (length + 3) & ~3;
        record[length..padded].Clear();
        glyf.Truncate(start + padded);
    }

    private static void WriteLocation(Span<byte> locations, int glyph, int offset, bool hasLongOffsets)
    {
        if (hasLongOffsets)
        {
            BinaryPrimitives.WriteUInt32BigEndian(locations[(glyph * 4)..], (uint)offset);
            return;
        }

        // The short format stores half the offset, in 16 bits.
        if (offset > 2 * ushort.MaxValue)
            throw FontErrors.InvalidTable(FontTags.Glyf, "is too large for the short format of glyph locations its transformed form declares.");

        BinaryPrimitives.WriteUInt16BigEndian(locations[(glyph * 2)..], (ushort)(offset / 2));
    }

    /// <summary>Writes the difference of a coordinate with the previous point, and returns the flags that describe it.</summary>
    private static int WriteDelta(int delta, Span<byte> destination, ref int length, int shortVector, int sameOrPositive)
    {
        switch (delta)
        {
            case 0:
                return sameOrPositive;

            case > -256 and < 256:
                destination[length++] = (byte)Math.Abs(delta);
                return shortVector | (delta > 0 ? sameOrPositive : 0);

            case >= short.MinValue and <= short.MaxValue:
                BinaryPrimitives.WriteInt16BigEndian(destination[length..], (short)delta);
                length += 2;
                return 0;

            default:
                throw FontErrors.InvalidTable(FontTags.Glyf, "has a point too far from the previous one.");
        }
    }

    /// <summary>
    /// Gets the number of bytes of the coordinates of a point: section 5.2, "Decoding of variable-length X and Y
    /// coordinates". The 128 values of a flag are five groups of encodings.
    /// </summary>
    private static int GetCoordinatesLength(int flag)
    {
        return (flag & 0x7F) switch
        {
            < 84 => 1,
            < 120 => 2,
            < 124 => 3,
            _ => 4,
        };
    }

    /// <summary>Decodes the coordinates of a point, relative to the previous point: section 5.2.</summary>
    private static void DecodeCoordinates(int flag, ReadOnlySpan<byte> data, out int dx, out int dy)
    {
        var index = flag & 0x7F;
        switch (index)
        {
            // 0 to 9: no x; the y is 8 bits and one of five bases, 256 apart. The sign alternates: negative first.
            case < 10:
                dx = 0;
                dy = Sign(index, ((index >> 1) << 8) + data[0]);
                break;

            // 10 to 19: the same for x, without y.
            case < 20:
                dx = Sign(index, (((index - 10) >> 1) << 8) + data[0]);
                dy = 0;
                break;

            // 20 to 83: 4 bits for each coordinate. Four bases for x, 16 apart from 1, each with four bases for y, each
            // with the four combinations of signs: x negative first, then y negative first.
            case < 84:
                {
                    var group = index - 20;
                    dx = Sign(group, 1 + ((group >> 4) << 4) + (data[0] >> 4));
                    dy = Sign(group >> 1, 1 + (((group >> 2) & 3) << 4) + (data[0] & 0x0F));
                    break;
                }

            // 84 to 119: 8 bits for each coordinate. Three bases for x, 256 apart from 1, each with three bases for y.
            case < 120:
                {
                    var group = index - 84;
                    dx = Sign(group, 1 + ((group / 12) << 8) + data[0]);
                    dy = Sign(group >> 1, 1 + (((group % 12) >> 2) << 8) + data[1]);
                    break;
                }

            // 120 to 123: 12 bits for each coordinate.
            case < 124:
                dx = Sign(index, (data[0] << 4) + (data[1] >> 4));
                dy = Sign(index >> 1, ((data[1] & 0x0F) << 8) + data[2]);
                break;

            // 124 to 127: 16 bits for each coordinate.
            default:
                dx = Sign(index, (data[0] << 8) + data[1]);
                dy = Sign(index >> 1, (data[2] << 8) + data[3]);
                break;
        }
    }

    /// <summary>Applies the sign of an encoding: the encodings of a group alternate, negative first.</summary>
    private static int Sign(int index, int magnitude) => (index & 1) != 0 ? magnitude : -magnitude;

    private static ReadOnlySpan<byte> Take(ReadOnlySpan<byte> table, scoped ref long offset, uint length)
    {
        var result = FontBinary.Slice(table, offset, length);
        offset += length;
        return result;
    }

    /// <summary>The positions in the streams of the transformed table.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct Cursors
    {
        public int PointCount;
        public int Flag;
        public int Glyph;
        public int Composite;
        public int Box;
        public int Instruction;
    }
}
