namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the <c>gvar</c> table: how the points of each TrueType glyph move along the axes of a variable font.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "gvar - Glyph Variations Table" and "Tuple variation store". A glyph has a list of tuple
/// variations; each one is a region of the variation space (a peak, and optionally where it starts and ends) and a
/// delta for some or all of the points of the glyph. The points of a glyph are those of its contours, or one point per
/// component of a composite glyph (its offset), followed by four phantom points: the origin, the advance, and two
/// points for the vertical metrics.
/// </para>
/// <para>
/// The deltas of an instance are sums, over the tuple variations, of the deltas multiplied by the scalar of the region.
/// When a tuple variation gives a delta for some of the points of a simple glyph only, the other points of each contour
/// follow the points that have one ("inferred deltas for un-referenced point numbers").
/// </para>
/// <para>Nothing is rounded: the points of an instance are fractions of font units.</para>
/// </remarks>
internal sealed class GvarTable
{
    private const int SharedPointNumbers = 0x8000;
    private const int TupleCountMask = 0x0FFF;
    private const int EmbeddedPeakTuple = 0x8000;
    private const int IntermediateRegion = 0x4000;
    private const int PrivatePointNumbers = 0x2000;
    private const int TupleIndexMask = 0x0FFF;

    private readonly int _axisCount;
    private readonly int _sharedTupleCount;
    private readonly int _sharedTuples;
    private readonly int _glyphCount;
    private readonly bool _hasLongOffsets;
    private readonly int _data;

    private GvarTable(int axisCount, int sharedTupleCount, int sharedTuples, int glyphCount, bool hasLongOffsets, int data)
    {
        _axisCount = axisCount;
        _sharedTupleCount = sharedTupleCount;
        _sharedTuples = sharedTuples;
        _glyphCount = glyphCount;
        _hasLongOffsets = hasLongOffsets;
        _data = data;
    }

    /// <summary>Reads the header of the table, or returns <see langword="null"/> when it cannot apply to the axes of the font.</summary>
    public static GvarTable? Read(ReadOnlySpan<byte> table, int axisCount)
    {
        if (table.Length < 20 || FontBinary.UInt16(table, 0) != 1 || FontBinary.UInt16(table, 4) != axisCount)
            return null;

        var sharedTupleCount = FontBinary.UInt16(table, 6);
        var sharedTuples = FontBinary.Offset32(table, 8);
        var glyphCount = FontBinary.UInt16(table, 12);
        var hasLongOffsets = (FontBinary.UInt16(table, 14) & 1) != 0;
        var data = FontBinary.Offset32(table, 16);
        FontBinary.EnsureRange(table, sharedTuples, (long)sharedTupleCount * axisCount * 2);
        FontBinary.EnsureRange(table, 20, ((long)glyphCount + 1) * (hasLongOffsets ? 4 : 2));
        return new GvarTable(axisCount, sharedTupleCount, sharedTuples, glyphCount, hasLongOffsets, data);
    }

    /// <summary>
    /// Adds, to sums of deltas, the deltas of the points of a glyph for an instance.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="glyphId">The glyph.</param>
    /// <param name="coordinates">The normalized coordinates of the instance, as 2.14 fixed-point numbers.</param>
    /// <param name="pointCount">The number of points of the glyph, with its four phantom points.</param>
    /// <param name="first">The first point the sums are for.</param>
    /// <param name="sumX">The sums of the horizontal deltas, for the points from <paramref name="first"/>.</param>
    /// <param name="sumY">The sums of the vertical deltas, for the same points.</param>
    /// <param name="contours">
    /// The points of the contours of a simple glyph, with their default coordinates, for which a delta is inferred when a
    /// tuple variation does not give one; empty for a composite glyph, or when the sums are not for these points.
    /// </param>
    /// <param name="work">Working storage of three numbers per point of <paramref name="contours"/>.</param>
    /// <param name="budget">The complexity budget of the glyph.</param>
    public void AddDeltas(ReadOnlySpan<byte> table, int glyphId, ReadOnlySpan<int> coordinates, int pointCount, int first, Span<double> sumX, Span<double> sumY, ReadOnlySpan<GlyphPoint> contours, Span<double> work, ref GlyphBudget budget)
    {
        var data = GetGlyphData(table, glyphId);
        if (data.Length < 4)
            return;

        var countField = FontBinary.UInt16(data, 0);
        var tupleCount = countField & TupleCountMask;
        var serialized = FontBinary.UInt16(data, 2);
        var header = 4;

        // The point numbers that the tuple variations without their own share.
        var sharedPoints = -1;
        if ((countField & SharedPointNumbers) != 0)
        {
            sharedPoints = serialized;
            serialized = PackedPointNumbers.Skip(data, serialized);
        }

        for (var tuple = 0; tuple < tupleCount; tuple++)
        {
            budget.Charge(1 + _axisCount);
            var size = FontBinary.UInt16(data, header);
            var index = FontBinary.UInt16(data, header + 2);
            header += 4;

            ReadOnlySpan<byte> peak;
            if ((index & EmbeddedPeakTuple) != 0)
            {
                peak = FontBinary.Slice(data, header, _axisCount * 2);
                header += _axisCount * 2;
            }
            else
            {
                var shared = index & TupleIndexMask;
                if (shared >= _sharedTupleCount)
                    throw FontErrors.InvalidTable(FontTags.Gvar, "refers to a shared tuple that does not exist.");

                peak = table.Slice(_sharedTuples + (shared * _axisCount * 2), _axisCount * 2);
            }

            ReadOnlySpan<byte> intermediate = default;
            if ((index & IntermediateRegion) != 0)
            {
                intermediate = FontBinary.Slice(data, header, _axisCount * 4);
                header += _axisCount * 4;
            }

            var tupleData = FontBinary.Slice(data, serialized, size);
            serialized += size;
            var scalar = GetScalar(peak, intermediate, coordinates);
            if (scalar == 0)
                continue;

            // The work is the points of the glyph, or the point numbers of the tuple variation when it names more
            // points than the glyph has.
            budget.Charge(pointCount);
            if ((index & PrivatePointNumbers) != 0)
            {
                var points = new PackedPointNumbers(tupleData, 0);
                var deltas = PackedPointNumbers.Skip(tupleData, 0);
                budget.Charge(points.Count);
                AddTuple(tupleData[deltas..], points, scalar, pointCount, first, sumX, sumY, contours, work);
            }
            else if (sharedPoints >= 0)
            {
                var points = new PackedPointNumbers(data, sharedPoints);
                budget.Charge(points.Count);
                AddTuple(tupleData, points, scalar, pointCount, first, sumX, sumY, contours, work);
            }
            else
            {
                AddTuple(tupleData, default, scalar, pointCount, first, sumX, sumY, contours, work);
            }
        }
    }

    private ReadOnlySpan<byte> GetGlyphData(ReadOnlySpan<byte> table, int glyphId)
    {
        if ((uint)glyphId >= (uint)_glyphCount)
            return default;

        long start;
        long end;
        if (_hasLongOffsets)
        {
            start = FontBinary.UInt32(table, 20 + (glyphId * 4));
            end = FontBinary.UInt32(table, 24 + (glyphId * 4));
        }
        else
        {
            // The short form stores half offsets.
            start = FontBinary.UInt16(table, 20 + (glyphId * 2)) * 2L;
            end = FontBinary.UInt16(table, 22 + (glyphId * 2)) * 2L;
        }

        if (end < start || _data + end > table.Length)
            throw FontErrors.InvalidTable(FontTags.Gvar, $"gives the variations of the glyph {glyphId} a location outside the table.");

        return table.Slice(_data + (int)start, (int)(end - start));
    }

    /// <summary>Gets the scalar of the region of a tuple variation: "Algorithm for interpolation of instance values".</summary>
    private double GetScalar(ReadOnlySpan<byte> peak, ReadOnlySpan<byte> intermediate, ReadOnlySpan<int> coordinates)
    {
        var scalar = 1.0;
        for (var axis = 0; axis < _axisCount; axis++)
        {
            var peakValue = FontBinary.Int16(peak, axis * 2);
            if (peakValue == 0)
                continue;

            var coordinate = axis < coordinates.Length ? coordinates[axis] : 0;
            if (coordinate == 0)
                return 0;

            if (coordinate == peakValue)
                continue;

            if (intermediate.IsEmpty)
            {
                // The region goes from the default to the peak: nothing on the other side of the default, nor beyond.
                if (coordinate < Math.Min(0, peakValue) || coordinate > Math.Max(0, peakValue))
                    return 0;

                scalar *= (double)coordinate / peakValue;
            }
            else
            {
                var start = FontBinary.Int16(intermediate, axis * 2);
                var end = FontBinary.Int16(intermediate, (_axisCount * 2) + (axis * 2));
                if (start > peakValue || peakValue > end || (start < 0 && end > 0))
                    continue;

                if (coordinate <= start || coordinate >= end)
                    return 0;

                scalar *= coordinate < peakValue
                    ? (double)(coordinate - start) / (peakValue - start)
                    : (double)(end - coordinate) / (end - peakValue);
            }
        }

        return scalar;
    }

    /// <summary>Adds the deltas of one tuple variation: the horizontal deltas of its points, then the vertical ones.</summary>
    private static void AddTuple(ReadOnlySpan<byte> deltas, PackedPointNumbers points, double scalar, int pointCount, int first, Span<double> sumX, Span<double> sumY, ReadOnlySpan<GlyphPoint> contours, Span<double> work)
    {
        var count = points.IsAllPoints ? pointCount : points.Count;
        var x = new PackedDeltas(deltas, 0);
        var y = new PackedDeltas(deltas, PackedDeltas.Skip(deltas, 0, count));
        if (points.IsAllPoints)
        {
            for (var i = 0; i < count; i++)
            {
                var dx = x.Read();
                var dy = y.Read();
                var target = i - first;
                if ((uint)target < (uint)sumX.Length)
                {
                    sumX[target] += scalar * dx;
                    sumY[target] += scalar * dy;
                }
            }

            return;
        }

        if (contours.IsEmpty)
        {
            // No point follows another one: the points without delta do not move.
            for (var i = 0; i < count; i++)
            {
                var target = points.Read() - first;
                var dx = x.Read();
                var dy = y.Read();
                if ((uint)target < (uint)sumX.Length)
                {
                    sumX[target] += scalar * dx;
                    sumY[target] += scalar * dy;
                }
            }

            return;
        }

        // The deltas of the tuple variation for each point of the contours, and whether it has one.
        var tupleX = work[..contours.Length];
        var tupleY = work.Slice(contours.Length, contours.Length);
        var touched = work.Slice(contours.Length * 2, contours.Length);
        tupleX.Clear();
        tupleY.Clear();
        touched.Clear();
        for (var i = 0; i < count; i++)
        {
            var point = points.Read();
            var dx = x.Read();
            var dy = y.Read();
            if ((uint)point < (uint)contours.Length)
            {
                tupleX[point] = dx;
                tupleY[point] = dy;
                touched[point] = 1;
            }
            else if ((uint)(point - first) < (uint)sumX.Length)
            {
                // A phantom point.
                sumX[point - first] += scalar * dx;
                sumY[point - first] += scalar * dy;
            }
        }

        var start = 0;
        for (var i = 0; i < contours.Length; i++)
        {
            if ((contours[i].Flags & GlyphPoint.EndOfContour) == 0 && i != contours.Length - 1)
                continue;

            InferDeltas(contours[start..(i + 1)], tupleX[start..(i + 1)], tupleY[start..(i + 1)], touched[start..(i + 1)]);
            start = i + 1;
        }

        for (var i = 0; i < contours.Length; i++)
        {
            var target = i - first;
            if ((uint)target < (uint)sumX.Length)
            {
                sumX[target] += scalar * tupleX[i];
                sumY[target] += scalar * tupleY[i];
            }
        }
    }

    /// <summary>Gives a delta to the points of a contour that have none, from the points before and after them that have one.</summary>
    private static void InferDeltas(ReadOnlySpan<GlyphPoint> contour, Span<double> deltaX, Span<double> deltaY, ReadOnlySpan<double> touched)
    {
        var firstTouched = -1;
        for (var i = 0; i < contour.Length; i++)
        {
            if (touched[i] != 0)
            {
                firstTouched = i;
                break;
            }
        }

        // A contour without any delta does not move.
        if (firstTouched < 0)
            return;

        // From each point that has a delta to the next one, around the contour: the points between them are inferred
        // from the two. With a single point that has a delta, it is both, and every point moves like it.
        var previous = firstTouched;
        do
        {
            var next = previous;
            do
            {
                next = next + 1 == contour.Length ? 0 : next + 1;
            }
            while (touched[next] == 0);

            for (var i = previous + 1 == contour.Length ? 0 : previous + 1; i != next; i = i + 1 == contour.Length ? 0 : i + 1)
            {
                deltaX[i] = Infer(contour[i].X, contour[previous].X, contour[next].X, deltaX[previous], deltaX[next]);
                deltaY[i] = Infer(contour[i].Y, contour[previous].Y, contour[next].Y, deltaY[previous], deltaY[next]);
            }

            previous = next;
        }
        while (previous != firstTouched);
    }

    /// <summary>
    /// Infers the delta of a coordinate from two reference points: between their coordinates, the delta is interpolated
    /// between theirs; outside, it is the delta of the nearest one.
    /// </summary>
    private static double Infer(double coordinate, double reference1, double reference2, double delta1, double delta2)
    {
        if (reference1 == reference2)
            return delta1 == delta2 ? delta1 : 0;

        if (coordinate <= Math.Min(reference1, reference2))
            return reference1 < reference2 ? delta1 : delta2;

        if (coordinate >= Math.Max(reference1, reference2))
            return reference1 > reference2 ? delta1 : delta2;

        return delta1 + ((delta2 - delta1) * ((coordinate - reference1) / (reference2 - reference1)));
    }
}
