namespace Meziantou.Framework.Fonts;

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
/// once per level. The contours are then emitted. The hinting instructions are skipped here: <see cref="TrueTypeHinter"/>
/// runs them.
/// </para>
/// <para>
/// A glyph also has phantom points, which are not drawn: its origin, at the left side bearing of the horizontal metrics
/// before the left of its bounding box, and its advance after the origin. The outline is placed so that the origin is
/// at zero, as a TrueType rasterizer does: a glyph whose left side bearing is not the left of its bounding box is
/// moved. A composite glyph can take the phantom points of one of its components.
/// </para>
/// <para>
/// In an instance of a variable font, the points of each glyph (those of a simple glyph, the offsets of the components
/// of a composite glyph, and the phantom points) are first moved by the deltas of the <c>gvar</c> table.
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
    internal const int ArgumentsAreWords = 0x0001;
    internal const int ArgumentsAreXYValues = 0x0002;
    internal const int HasScale = 0x0008;
    internal const int RoundXYToGrid = 0x0004;
    internal const int MoreComponents = 0x0020;
    internal const int WeHaveInstructions = 0x0100;
    internal const int UseMyMetrics = 0x0200;
    internal const int HasXAndYScale = 0x0040;
    internal const int HasTwoByTwo = 0x0080;
    internal const int ScaledComponentOffset = 0x0800;
    internal const int UnscaledComponentOffset = 0x1000;

    /// <summary>Decodes a glyph into a sink.</summary>
    /// <exception cref="InvalidFontException">The data of the glyph is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit or the allocation limit.</exception>
    public static void Decode(in GlyfContext context, int glyphId, OutlineSink sink, GlyphScratch scratch, ref GlyphBudget budget)
    {
        scratch.Reset();
        Collect(context, glyphId, scratch, ref budget, depth: 0, out var origin, out _);
        var points = scratch.Points;
        if (origin != 0)
        {
            for (var i = 0; i < points.Length; i++)
            {
                points[i].X -= origin;
            }
        }

        Emit(points, sink);
    }

    /// <summary>
    /// Gets the advance of a glyph of an instance of a variable font from its phantom points: the way a font without
    /// <c>HVAR</c> table varies its advances.
    /// </summary>
    public static double GetAdvance(in GlyfContext context, int glyphId, ref GlyphBudget budget, int depth = 0)
    {
        GetMetrics(context, glyphId, out var advance, out _);
        var data = GetGlyphData(context, glyphId);
        var pointCount = 0;
        if (data.Length >= 10)
        {
            var contourCount = FontBinary.Int16(data, 0);
            if (contourCount > 0)
            {
                pointCount = FontBinary.UInt16(data, 10 + ((contourCount - 1) * 2)) + 1;
            }
            else if (contourCount < 0)
            {
                if (depth >= MaxCompositeDepth)
                    throw FontErrors.InvalidTable(FontTags.Glyf, "has composite glyphs nested too deeply, or that refer to each other.");

                // The component whose metrics the glyph uses, if any: the last one that says so.
                var metricsComponent = -1;
                var position = 10;
                while (true)
                {
                    budget.Charge(1);
                    var flags = FontBinary.UInt16(data, position);
                    if ((flags & UseMyMetrics) != 0)
                    {
                        metricsComponent = FontBinary.UInt16(data, position + 2);
                    }

                    position += GetComponentLength(flags);
                    pointCount++;
                    if ((flags & MoreComponents) == 0)
                        break;
                }

                if (metricsComponent >= 0)
                {
                    if (metricsComponent >= context.GlyphCount)
                        throw FontErrors.InvalidTable(FontTags.Glyf, $"has a composite glyph that refers to the glyph {metricsComponent}, which the font does not have.");

                    return GetAdvance(context, metricsComponent, ref budget, depth + 1);
                }
            }
        }

        if (context.Variations is null)
            return advance;

        // Only the deltas of the origin and of the advance are needed: the two first phantom points.
        Span<double> sumX = stackalloc double[4];
        Span<double> sumY = stackalloc double[4];
        sumX.Clear();
        sumY.Clear();
        context.Variations.AddDeltas(context.Gvar, glyphId, context.Coordinates, pointCount + 4, pointCount, sumX, sumY, contours: default, work: default, ref budget);
        return advance + sumX[1] - sumX[0];
    }

    internal static ReadOnlySpan<byte> GetGlyphData(in GlyfContext context, int glyphId) => context.Glyf.Slice(GetGlyphOffset(context, glyphId, out var length), length);

    /// <summary>Gets where the data of a glyph starts in the <c>glyf</c> table, and its length.</summary>
    internal static int GetGlyphOffset(in GlyfContext context, int glyphId, out int length)
    {
        var loca = context.Loca;
        long start;
        long end;
        if (context.HasLongLocations)
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

        if (end < start || end > context.Glyf.Length)
            throw FontErrors.InvalidTable(FontTags.Loca, $"gives the glyph {glyphId} a location outside the 'glyf' table.");

        length = (int)(end - start);
        return (int)start;
    }

    /// <summary>Reads the horizontal metrics of a glyph: the glyphs after the last metric have its advance, and their own bearing.</summary>
    internal static void GetMetrics(in GlyfContext context, int glyphId, out int advance, out int leftSideBearing)
    {
        var hmtx = context.Hmtx;
        if (glyphId < context.MetricCount)
        {
            advance = FontBinary.UInt16(hmtx, glyphId * 4);
            leftSideBearing = FontBinary.Int16(hmtx, (glyphId * 4) + 2);
            return;
        }

        advance = FontBinary.UInt16(hmtx, (context.MetricCount - 1) * 4);
        var bearing = (context.MetricCount * 4) + ((glyphId - context.MetricCount) * 2);

        // A table that stops before the bearing of the glyph gives it none.
        leftSideBearing = bearing <= hmtx.Length - 2 ? FontBinary.Int16(hmtx, bearing) : 0;
    }

    /// <summary>Gets the length of the record of a component of a composite glyph.</summary>
    internal static int GetComponentLength(int flags)
    {
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

        return length;
    }

    /// <summary>
    /// Appends the points of a glyph to the scratch, in the coordinate space of the glyph, and gives its origin and its
    /// advance point.
    /// </summary>
    private static void Collect(in GlyfContext context, int glyphId, GlyphScratch scratch, ref GlyphBudget budget, int depth, out double origin, out double advancePoint)
    {
        var data = GetGlyphData(context, glyphId);
        GetMetrics(context, glyphId, out var advance, out var leftSideBearing);
        var variations = context.Variations;
        if (data.IsEmpty)
        {
            // A glyph without outline still has phantom points, which can vary.
            origin = -leftSideBearing;
            advancePoint = origin + advance;
            if (variations is not null)
            {
                Span<double> sumX = stackalloc double[4];
                Span<double> sumY = stackalloc double[4];
                sumX.Clear();
                sumY.Clear();
                variations.AddDeltas(context.Gvar, glyphId, context.Coordinates, 4, 0, sumX, sumY, contours: default, work: default, ref budget);
                origin += sumX[0];
                advancePoint += sumX[1];
            }

            return;
        }

        // The header: the number of contours, then the bounding box, whose left side places the origin.
        var contourCount = FontBinary.Int16(data, 0);
        origin = FontBinary.Int16(data, 2) - leftSideBearing;
        advancePoint = origin + advance;
        if (contourCount >= 0)
        {
            var first = scratch.Count;
            CollectSimple(data, contourCount, scratch, ref budget);
            if (variations is not null)
            {
                // The sums of the deltas of the points and of the phantom points, then what a tuple variation needs
                // to infer the deltas of the points it does not give.
                var count = scratch.Count - first;
                var total = count + 4;
                var workStart = scratch.RentWork((2L * total) + (3L * count));
                var work = scratch.Work[workStart..];
                var sumX = work[..total];
                var sumY = work.Slice(total, total);
                var points = scratch.Points[first..];
                variations.AddDeltas(context.Gvar, glyphId, context.Coordinates, total, 0, sumX, sumY, points, work[(2 * total)..], ref budget);
                for (var i = 0; i < points.Length; i++)
                {
                    points[i].X += sumX[i];
                    points[i].Y += sumY[i];
                }

                origin += sumX[count];
                advancePoint += sumX[count + 1];
                scratch.ReturnWork(workStart);
            }

            return;
        }

        if (depth >= MaxCompositeDepth)
            throw FontErrors.InvalidTable(FontTags.Glyf, "has composite glyphs nested too deeply, or that refer to each other.");

        // In an instance, the offset of each component and the phantom points have deltas: one point per component.
        var deltas = -1;
        var componentCount = 0;
        if (variations is not null)
        {
            for (var record = 10; ;)
            {
                budget.Charge(1);
                var flags = FontBinary.UInt16(data, record);
                record += GetComponentLength(flags);
                componentCount++;
                if ((flags & MoreComponents) == 0)
                    break;
            }

            var total = componentCount + 4;
            deltas = scratch.RentWork(2L * total);
            var work = scratch.Work[deltas..];
            variations.AddDeltas(context.Gvar, glyphId, context.Coordinates, total, 0, work[..total], work.Slice(total, total), contours: default, work: default, ref budget);
            origin += work[componentCount];
            advancePoint += work[componentCount + 1];
        }

        var componentNumber = 0;
        var glyphStart = scratch.Count;
        var position = 10;
        while (true)
        {
            budget.Charge(1);
            var flags = FontBinary.UInt16(data, position);
            var component = FontBinary.UInt16(data, position + 2);
            position += 4;
            if (component >= context.GlyphCount)
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
            Collect(context, component, scratch, ref budget, depth + 1, out var componentOrigin, out var componentAdvancePoint);
            if ((flags & UseMyMetrics) != 0)
            {
                origin = componentOrigin;
                advancePoint = componentAdvancePoint;
            }

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
                if (deltas >= 0)
                {
                    var work = scratch.Work[deltas..];
                    offsetX += work[componentNumber];
                    offsetY += work[componentCount + 4 + componentNumber];
                }

                // The offset is in the space of the composite glyph, unless the font asks for the transformation of
                // the component to apply to it too.
                if (isTransformed && (flags & ScaledComponentOffset) != 0 && (flags & UnscaledComponentOffset) == 0)
                {
                    var x = offsetX;
                    var y = offsetY;
                    offsetX = (a * x) + (c * y);
                    offsetY = (b * x) + (d * y);
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

            componentNumber++;
            if ((flags & MoreComponents) == 0)
                break;
        }

        if (deltas >= 0)
        {
            scratch.ReturnWork(deltas);
        }
    }

    private static void CollectSimple(ReadOnlySpan<byte> data, int contourCount, GlyphScratch scratch, ref GlyphBudget budget) => CollectSimple(data, contourCount, scratch, ref budget, out _, out _);

    /// <summary>Appends the points of a simple glyph to the scratch, and gives where its instructions are in its data.</summary>
    internal static void CollectSimple(ReadOnlySpan<byte> data, int contourCount, GlyphScratch scratch, ref GlyphBudget budget, out int instructionOffset, out int instructionLength)
    {
        instructionOffset = 0;
        instructionLength = 0;
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

        // The hinting instructions, which only a hinter runs.
        instructionLength = FontBinary.UInt16(data, position);
        instructionOffset = position + 2;
        position += 2 + instructionLength;

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
    internal static void Emit(ReadOnlySpan<GlyphPoint> points, OutlineSink sink)
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
