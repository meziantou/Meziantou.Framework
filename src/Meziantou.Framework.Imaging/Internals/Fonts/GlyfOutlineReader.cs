namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Decodes the TrueType outlines of the <c>glyf</c> table.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "glyf - Glyph Data" and "loca - Index to Location". A glyph is a set of contours of on-curve
/// and off-curve points (quadratic Bézier curves), or a composite of other glyphs, each placed by an offset or by
/// matching two points, and optionally transformed.
/// </para>
/// <para>
/// The points of the glyph are first gathered, in the coordinate space of the glyph, in the scratch storage; the
/// points of a component are transformed in place once the component is read, so a nested composite is transformed
/// once per level. The contours are then emitted. Hinting instructions are skipped: outlines are not grid-fitted.
/// </para>
/// </remarks>
internal static class GlyfOutlineReader
{
    /// <summary>The greatest nesting of composite glyphs. The complexity budget bounds the total work.</summary>
    public const int MaxCompositeDepth = 64;

    // Flags of a point of a simple glyph
    private const int OnCurvePoint = 0x01;
    private const int XShortVector = 0x02;
    private const int YShortVector = 0x04;
    private const int RepeatFlag = 0x08;
    private const int XIsSameOrPositive = 0x10;
    private const int YIsSameOrPositive = 0x20;

    // Flags of a component of a composite glyph
    private const int ArgumentsAreWords = 0x0001;
    private const int ArgumentsAreXYValues = 0x0002;
    private const int HasScale = 0x0008;
    private const int MoreComponents = 0x0020;
    private const int HasXAndYScale = 0x0040;
    private const int HasTwoByTwo = 0x0080;
    private const int ScaledComponentOffset = 0x0800;
    private const int UnscaledComponentOffset = 0x1000;

    /// <summary>Decodes a glyph into a sink.</summary>
    /// <exception cref="InvalidImageContentException">The data of the glyph is malformed.</exception>
    /// <exception cref="ImageResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public static void Decode(ReadOnlySpan<byte> loca, ReadOnlySpan<byte> glyf, bool longOffsets, int glyphCount, int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        scratch.Reset();
        Collect(loca, glyf, longOffsets, glyphCount, glyphId, scratch, ref budget, depth: 0);
        Emit(scratch.Points, sink);
    }

    private static ReadOnlySpan<byte> GetGlyphData(ReadOnlySpan<byte> loca, ReadOnlySpan<byte> glyf, bool longOffsets, int glyphId)
    {
        long start;
        long end;
        if (longOffsets)
        {
            start = FontBinary.UInt32(loca, glyphId * 4);
            end = FontBinary.UInt32(loca, (glyphId * 4) + 4);
        }
        else
        {
            // The short form stores half offsets.
            start = FontBinary.UInt16(loca, glyphId * 2) * 2L;
            end = FontBinary.UInt16(loca, (glyphId * 2) + 2) * 2L;
        }

        if (end < start || end > glyf.Length)
            throw FontErrors.InvalidTable(FontTags.Loca, $"gives the glyph {glyphId} a location outside the 'glyf' table.");

        return glyf.Slice((int)start, (int)(end - start));
    }

    /// <summary>Appends the points of a glyph to the scratch, in the coordinate space of the glyph.</summary>
    private static void Collect(ReadOnlySpan<byte> loca, ReadOnlySpan<byte> glyf, bool longOffsets, int glyphCount, int glyphId, GlyphScratch scratch, ref GlyphBudget budget, int depth)
    {
        var data = GetGlyphData(loca, glyf, longOffsets, glyphId);
        if (data.IsEmpty)
            return;

        // The header: the number of contours, then a bounding box that the outline does not need.
        var contourCount = FontBinary.Int16(data, 0);
        if (contourCount >= 0)
        {
            CollectSimple(data, contourCount, scratch, ref budget);
            return;
        }

        if (depth >= MaxCompositeDepth)
            throw FontErrors.InvalidTable(FontTags.Glyf, "has composite glyphs nested too deeply, or that refer to each other.");

        var glyphStart = scratch.Count;
        var position = 10;
        while (true)
        {
            budget.Charge(1);
            var flags = FontBinary.UInt16(data, position);
            var component = FontBinary.UInt16(data, position + 2);
            position += 4;
            if (component >= glyphCount)
                throw FontErrors.InvalidTable(FontTags.Glyf, $"has a composite glyph that refers to the glyph {component}, which the font does not have.");

            int argument1;
            int argument2;
            if ((flags & ArgumentsAreWords) != 0)
            {
                if ((flags & ArgumentsAreXYValues) != 0)
                {
                    argument1 = FontBinary.Int16(data, position);
                    argument2 = FontBinary.Int16(data, position + 2);
                }
                else
                {
                    argument1 = FontBinary.UInt16(data, position);
                    argument2 = FontBinary.UInt16(data, position + 2);
                }

                position += 4;
            }
            else
            {
                if ((flags & ArgumentsAreXYValues) != 0)
                {
                    argument1 = FontBinary.Int8(data, position);
                    argument2 = FontBinary.Int8(data, position + 1);
                }
                else
                {
                    argument1 = FontBinary.UInt8(data, position);
                    argument2 = FontBinary.UInt8(data, position + 1);
                }

                position += 2;
            }

            // The transformation: x' = a x + c y, y' = b x + d y.
            double a = 1;
            double b = 0;
            double c = 0;
            double d = 1;
            if ((flags & HasScale) != 0)
            {
                a = d = FontBinary.Int16(data, position) / 16384.0;
                position += 2;
            }
            else if ((flags & HasXAndYScale) != 0)
            {
                a = FontBinary.Int16(data, position) / 16384.0;
                d = FontBinary.Int16(data, position + 2) / 16384.0;
                position += 4;
            }
            else if ((flags & HasTwoByTwo) != 0)
            {
                a = FontBinary.Int16(data, position) / 16384.0;
                b = FontBinary.Int16(data, position + 2) / 16384.0;
                c = FontBinary.Int16(data, position + 4) / 16384.0;
                d = FontBinary.Int16(data, position + 6) / 16384.0;
                position += 8;
            }

            var componentStart = scratch.Count;
            Collect(loca, glyf, longOffsets, glyphCount, component, scratch, ref budget, depth + 1);
            var points = scratch.Points;
            var isTransformed = a != 1 || b != 0 || c != 0 || d != 1;
            if (isTransformed)
            {
                for (var i = componentStart; i < points.Length; i++)
                {
                    var x = points[i].X;
                    var y = points[i].Y;
                    points[i].X = (a * x) + (c * y);
                    points[i].Y = (b * x) + (d * y);
                }
            }

            double offsetX;
            double offsetY;
            if ((flags & ArgumentsAreXYValues) != 0)
            {
                offsetX = argument1;
                offsetY = argument2;

                // The offset is in the space of the composite glyph, unless the font asks for the transformation of
                // the component to apply to it too.
                if (isTransformed && (flags & ScaledComponentOffset) != 0 && (flags & UnscaledComponentOffset) == 0)
                {
                    offsetX = (a * argument1) + (c * argument2);
                    offsetY = (b * argument1) + (d * argument2);
                }
            }
            else
            {
                // A point of the component is moved onto a point of the glyph assembled so far.
                var parentIndex = glyphStart + argument1;
                var componentIndex = componentStart + argument2;
                if (parentIndex >= componentStart || componentIndex >= points.Length)
                    throw FontErrors.InvalidTable(FontTags.Glyf, "has a composite glyph that matches a point that does not exist.");

                offsetX = points[parentIndex].X - points[componentIndex].X;
                offsetY = points[parentIndex].Y - points[componentIndex].Y;
            }

            if (offsetX != 0 || offsetY != 0)
            {
                for (var i = componentStart; i < points.Length; i++)
                {
                    points[i].X += offsetX;
                    points[i].Y += offsetY;
                }
            }

            if ((flags & MoreComponents) == 0)
                break;
        }
    }

    private static void CollectSimple(ReadOnlySpan<byte> data, int contourCount, GlyphScratch scratch, ref GlyphBudget budget)
    {
        if (contourCount == 0)
            return;

        // The index of the last point of each contour, in increasing order.
        var position = 10;
        var pointCount = 0;
        for (var i = 0; i < contourCount; i++)
        {
            var end = FontBinary.UInt16(data, position + (i * 2)) + 1;
            if (end <= pointCount)
                throw FontErrors.InvalidTable(FontTags.Glyf, "has a glyph whose contours are not in increasing order of points.");

            pointCount = end;
        }

        budget.Charge(pointCount);
        position += contourCount * 2;

        // The hinting instructions are skipped.
        position += 2 + FontBinary.UInt16(data, position);

        var first = scratch.Add(pointCount);
        var points = scratch.Points[first..];

        // The flags, where a flag can be repeated; then the x coordinates, then the y coordinates, as differences.
        for (var i = 0; i < pointCount;)
        {
            var flag = FontBinary.UInt8(data, position++);
            var repeat = (flag & RepeatFlag) != 0 ? FontBinary.UInt8(data, position++) : 0;
            if (i + repeat >= pointCount)
                throw FontErrors.InvalidTable(FontTags.Glyf, "has a glyph with more flags than points.");

            for (var j = 0; j <= repeat; j++)
            {
                points[i++].Flags = flag;
            }
        }

        var value = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var flag = points[i].Flags;
            if ((flag & XShortVector) != 0)
            {
                var delta = FontBinary.UInt8(data, position++);
                value += (flag & XIsSameOrPositive) != 0 ? delta : -delta;
            }
            else if ((flag & XIsSameOrPositive) == 0)
            {
                value += FontBinary.Int16(data, position);
                position += 2;
            }

            points[i].X = value;
        }

        value = 0;
        for (var i = 0; i < pointCount; i++)
        {
            var flag = points[i].Flags;
            if ((flag & YShortVector) != 0)
            {
                var delta = FontBinary.UInt8(data, position++);
                value += (flag & YIsSameOrPositive) != 0 ? delta : -delta;
            }
            else if ((flag & YIsSameOrPositive) == 0)
            {
                value += FontBinary.Int16(data, position);
                position += 2;
            }

            points[i].Y = value;
            points[i].Flags = (flag & OnCurvePoint) != 0 ? GlyphPoint.OnCurve : 0;
        }

        for (var i = 0; i < contourCount; i++)
        {
            points[FontBinary.UInt16(data, 10 + (i * 2))].Flags |= GlyphPoint.EndOfContour;
        }
    }

    /// <summary>Emits the contours of gathered points as lines and quadratic curves.</summary>
    private static void Emit(ReadOnlySpan<GlyphPoint> points, OutlineSink sink)
    {
        var start = 0;
        for (var i = 0; i < points.Length; i++)
        {
            // The transformations of nested composite glyphs can take a point anywhere.
            FontErrors.EnsureCoordinates(points[i].X, points[i].Y);
            if ((points[i].Flags & GlyphPoint.EndOfContour) == 0)
                continue;

            EmitContour(points[start..(i + 1)], sink);
            start = i + 1;
        }
    }

    private static void EmitContour(ReadOnlySpan<GlyphPoint> contour, OutlineSink sink)
    {
        if (contour.IsEmpty)
            return;

        // The contour starts at an on-curve point: its first point, else its last point, else the middle of the two
        // (two consecutive off-curve points have an on-curve point half way between them).
        var first = contour[0];
        var last = contour[^1];
        double startX;
        double startY;
        var from = 0;
        var to = contour.Length;
        if ((first.Flags & GlyphPoint.OnCurve) != 0)
        {
            startX = first.X;
            startY = first.Y;
            from = 1;
        }
        else if ((last.Flags & GlyphPoint.OnCurve) != 0)
        {
            startX = last.X;
            startY = last.Y;
            to = contour.Length - 1;
        }
        else
        {
            startX = (first.X + last.X) / 2;
            startY = (first.Y + last.Y) / 2;
        }

        sink.MoveTo(startX, startY);
        var hasControl = false;
        double controlX = 0;
        double controlY = 0;
                for (var i = from; i < to; i++)
        {
            var point = contour[i];
            if ((point.Flags & GlyphPoint.OnCurve) != 0)
            {
                if (hasControl)
                {
                    sink.QuadraticTo(controlX, controlY, point.X, point.Y);
                    hasControl = false;
                }
                else
                {
                    sink.LineTo(point.X, point.Y);
                }

            }
            else
            {
                if (hasControl)
                {
                    sink.QuadraticTo(controlX, controlY, (controlX + point.X) / 2, (controlY + point.Y) / 2);
                }

                controlX = point.X;
                controlY = point.Y;
                hasControl = true;
            }
        }

        if (hasControl)
        {
            sink.QuadraticTo(controlX, controlY, startX, startY);
        }
        else
        {
            // The line that closes the contour is always emitted, even when it has no length: a contour of one point,
            // which fonts use as an anchor, is a point with a line to itself. HarfBuzz and FreeType do the same, and a
            // shape made of the outline is the same whichever reader produced it.
            sink.LineTo(startX, startY);
        }

        sink.Close();
    }
}
