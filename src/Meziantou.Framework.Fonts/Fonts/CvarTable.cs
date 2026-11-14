namespace Meziantou.Framework.Fonts;

/// <summary>Reads the <c>cvar</c> table: how the control values of the <c>cvt </c> table vary along the axes of a variable font.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "cvar - CVT Variations Table" and "Tuple variation store". The table has one list of tuple
/// variations for the whole <c>cvt </c> table: each one is a region of the variation space, which the variation gives
/// itself (there are no shared tuples), and a delta for some or all of the control values. The point numbers of the
/// tuple variation store are the indexes of the control values; a control value a variation gives no delta for does not
/// move (nothing is inferred).
/// </para>
/// <para>
/// The control value of an instance is the value of the <c>cvt </c> table plus the sum, over the tuple variations, of
/// the deltas multiplied by the scalar of their region (<see cref="GvarTable.GetScalar"/>). The table is read when the
/// hinter of an instance is created; a table of another major version is ignored.
/// </para>
/// </remarks>
internal static class CvarTable
{
    private const int SharedPointNumbers = 0x8000;
    private const int TupleCountMask = 0x0FFF;
    private const int EmbeddedPeakTuple = 0x8000;
    private const int IntermediateRegion = 0x4000;
    private const int PrivatePointNumbers = 0x2000;

    /// <summary>Adds, to sums of deltas, the deltas of the control values for an instance.</summary>
    /// <param name="table">The table.</param>
    /// <param name="axisCount">The number of axes of the font.</param>
    /// <param name="coordinates">The normalized coordinates of the instance, as 2.14 fixed-point numbers.</param>
    /// <param name="sums">The sums, one per control value of the <c>cvt </c> table.</param>
    /// <param name="budget">The complexity budget: each tuple variation and each delta it gives is charged.</param>
    /// <exception cref="InvalidFontException">The table is malformed.</exception>
    /// <exception cref="FontResourceLimitException">The table exceeds the complexity limit.</exception>
    public static void AddDeltas(ReadOnlySpan<byte> table, int axisCount, ReadOnlySpan<int> coordinates, Span<double> sums, ref GlyphBudget budget)
    {
        if (table.Length < 8 || FontBinary.UInt16(table, 0) != 1)
            return;

        var countField = FontBinary.UInt16(table, 4);
        var tupleCount = countField & TupleCountMask;
        var serialized = FontBinary.UInt16(table, 6);
        var header = 8;

        // The point numbers that the tuple variations without their own share.
        var sharedPoints = -1;
        if ((countField & SharedPointNumbers) != 0)
        {
            sharedPoints = serialized;
            serialized = PackedPointNumbers.Skip(table, serialized);
        }

        for (var tuple = 0; tuple < tupleCount; tuple++)
        {
            budget.Charge(1 + axisCount);
            var size = FontBinary.UInt16(table, header);
            var index = FontBinary.UInt16(table, header + 2);
            header += 4;
            if ((index & EmbeddedPeakTuple) == 0)
                throw FontErrors.InvalidTable(FontTags.Cvar, "has a tuple variation without its peak: the table has no shared tuples.");

            var peak = FontBinary.Slice(table, header, axisCount * 2);
            header += axisCount * 2;
            ReadOnlySpan<byte> intermediate = default;
            if ((index & IntermediateRegion) != 0)
            {
                intermediate = FontBinary.Slice(table, header, axisCount * 4);
                header += axisCount * 4;
            }

            var data = FontBinary.Slice(table, serialized, size);
            serialized += size;
            var scalar = GvarTable.GetScalar(peak, intermediate, coordinates, axisCount);
            if (scalar == 0)
                continue;

            if ((index & PrivatePointNumbers) != 0)
            {
                AddTuple(data[PackedPointNumbers.Skip(data, 0)..], new PackedPointNumbers(data, 0), scalar, sums, ref budget);
            }
            else if (sharedPoints >= 0)
            {
                AddTuple(data, new PackedPointNumbers(table, sharedPoints), scalar, sums, ref budget);
            }
            else
            {
                AddTuple(data, default, scalar, sums, ref budget);
            }
        }
    }

    /// <summary>Adds the deltas of one tuple variation; a point number beyond the control values is ignored.</summary>
    private static void AddTuple(ReadOnlySpan<byte> data, PackedPointNumbers points, double scalar, Span<double> sums, ref GlyphBudget budget)
    {
        var count = points.IsAllPoints ? sums.Length : points.Count;
        budget.Charge(count);
        var deltas = new PackedDeltas(data, 0);
        for (var i = 0; i < count; i++)
        {
            var target = points.IsAllPoints ? i : points.Read();
            var delta = deltas.Read();
            if ((uint)target < (uint)sums.Length)
            {
                sums[target] += scalar * delta;
            }
        }
    }
}
