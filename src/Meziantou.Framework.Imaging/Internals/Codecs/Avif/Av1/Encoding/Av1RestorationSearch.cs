namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Chooses the parameters of the loop restoration of a frame (AV1 specification, section 7.17): for each unit of each
/// plane, a Wiener filter or none. The specification defines the filter for given coefficients; the encoder looks for
/// the coefficients that bring the unit closest to the frame it encodes.
/// </summary>
/// <remarks>
/// <para>
/// The filter of a unit is separable: 7 symmetric taps across, then 7 down, whose sum is 1, so each direction has 3
/// free coefficients (2 for a chroma plane, whose outer taps are zero). With <c>d[j]</c> the sum of the two samples at
/// the distance <c>3 - j</c> minus twice the center sample, the output of a direction is the center sample plus the sum
/// of <c>c[j] d[j]</c>: the coefficients that minimize the squared error against the source are the solution of the
/// normal equations of these sums, which are accumulated in integers over the unit. The horizontal coefficients are
/// solved first, as if there were no vertical filter, then the vertical ones on the samples that the horizontal filter
/// gives. Both are rounded to the 1/128 of the syntax and clamped to their ranges.
/// </para>
/// <para>
/// The solution is a proposal: the filter of the decoder is then applied to the unit with these coefficients, and the
/// unit keeps it when the squared error that it removes is worth the bits of its coefficients. The samples are read
/// through <see cref="Av1PostFilter.TryGetRestorationStripe"/>, the way the decoder reads them.
/// </para>
/// </remarks>
internal static class Av1RestorationSearch
{
    // The sums of a unit: the 6 products of the upper triangle of the normal matrix, the 3 of its right-hand side
    private const int UnitSums = 9;

    // The bits of the coefficients of a unit, in units of 1/256 bit: about 4 bits each, which the cost is not sensitive to
    private const long CoefficientRate = 4 << 8;

    /// <summary>Chooses the filter of every loop restoration unit of <paramref name="frame"/>, and stores it in the units of <paramref name="state"/>.</summary>
    /// <param name="scope">The allocation scope of the operation.</param>
    /// <param name="postFilter">The filters of the decoder, which saved the rows of the deblocked frame.</param>
    /// <param name="frame">The reconstructed frame, after the loop filter and CDEF.</param>
    /// <param name="state">The block-level state of the frame.</param>
    /// <param name="workspace">The source planes and the buffers of the search.</param>
    /// <param name="decisions">The cost of a rate against a distortion.</param>
    /// <param name="cancellationToken">The cancellation token of the operation.</param>
    /// <exception cref="ImageResourceLimitException">The working buffers exceed the live-allocation limit.</exception>
    public static void Run(AllocationScope scope, Av1PostFilter postFilter, Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, in Av1EncoderDecisions decisions, CancellationToken cancellationToken)
    {
        var header = frame.Header;
        postFilter.PrepareRestoration(scope, frame);
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            if (header.FrameRestorationType[plane] is Av1Constants.RestoreNone)
                continue;

            var unitCount = state.GetLoopRestorationUnitRows(plane) * state.GetLoopRestorationUnitCols(plane);
            var units = state.GetLoopRestorationUnits(plane);
            units.Clear();

            // The horizontal coefficients, then the vertical ones on the rows that they filter
            var sums = workspace.GetRestorationStatistics(unitCount * UnitSums);
            Accumulate(postFilter, frame, state, workspace, plane, vertical: false, sums, cancellationToken);
            Solve(plane, sums, units, pass: 1);
            Accumulate(postFilter, frame, state, workspace, plane, vertical: true, sums, cancellationToken);
            Solve(plane, sums, units, pass: 0);

            // The error of each unit as it is, and with its filter
            var errors = workspace.GetRestorationStatistics(unitCount * 2);
            Measure(postFilter, frame, state, workspace, plane, errors, cancellationToken);
            var coefficients = plane is 0 ? 6 : 4;
            for (var unit = 0; unit < unitCount; unit++)
            {
                var parameters = units.Slice(unit * Av1FrameState.LoopRestorationUnitLength, Av1FrameState.LoopRestorationUnitLength);
                var hasFilter = parameters.Slice(1, 6).ContainsAnyExcept((sbyte)0);
                var none = decisions.GetCost(errors[2 * unit], 1 << 8);
                var wiener = decisions.GetCost(errors[(2 * unit) + 1], (1 << 8) + (coefficients * CoefficientRate));
                if (hasFilter && wiener < none)
                {
                    parameters[0] = Av1Constants.RestoreWiener;
                }
                else
                {
                    parameters.Clear();
                }
            }
        }
    }

    // The sums of the normal equations of a direction, for each unit of the plane
    private static void Accumulate(Av1PostFilter postFilter, Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, int plane, bool vertical, Span<long> sums, CancellationToken cancellationToken)
    {
        sums.Clear();
        var header = frame.Header;
        var sequence = frame.Sequence;
        var subX = plane > 0 ? sequence.SubsamplingX : 0;
        var subY = plane > 0 ? sequence.SubsamplingY : 0;
        var planeWidth = (header.UpscaledWidth + subX) >> subX;
        var unitSize = header.LoopRestorationSize[plane];
        var unitRows = state.GetLoopRestorationUnitRows(plane);
        var unitCols = state.GetLoopRestorationUnitCols(plane);
        var units = state.GetLoopRestorationUnits(plane);
        var original = workspace.GetSource(plane);
        const int Padding = Av1LoopRestoration.Padding;
        Span<long> d = stackalloc long[3];
        var stripeCount = Av1PostFilter.GetRestorationStripeCount(frame, plane);
        for (var stripe = 0; stripe < stripeCount; stripe++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!postFilter.TryGetRestorationStripe(frame, plane, stripe, out var source, out var sourceStride, out var y0, out var height))
                continue;

            var unitRow = Math.Min(unitRows - 1, (((y0 << subY) + 8) >> subY) / unitSize);
            if (!vertical)
            {
                for (var y = 0; y < height; y++)
                {
                    var row = source.Slice(((y + Padding) * sourceStride) + Padding - 3, planeWidth + 6);
                    var target = original.GetRow(y0 + y);
                    for (var x = 0; x < planeWidth; x++)
                    {
                        var unit = sums.Slice(((unitRow * unitCols) + Math.Min(unitCols - 1, x / unitSize)) * UnitSums, UnitSums);
                        long center = row[x + 3];
                        d[0] = row[x] + row[x + 6] - (2 * center);
                        d[1] = row[x + 1] + row[x + 5] - (2 * center);
                        d[2] = row[x + 2] + row[x + 4] - (2 * center);
                        Add(unit, d, target[x] - center);
                    }
                }

                continue;
            }

            // The rows that the horizontal filter of each unit gives, with 7 fractional bits
            var filtered = workspace.GetFilteredRows((height + (2 * Padding)) * planeWidth);
            for (var y = 0; y < height + (2 * Padding); y++)
            {
                var row = source.Slice((y * sourceStride) + Padding - 3, planeWidth + 6);
                var output = filtered.Slice(y * planeWidth, planeWidth);
                for (var x = 0; x < planeWidth; x++)
                {
                    var horizontal = units.Slice((((unitRow * unitCols) + Math.Min(unitCols - 1, x / unitSize)) * Av1FrameState.LoopRestorationUnitLength) + 4, 3);
                    var center = row[x + 3];
                    output[x] = (128 * center)
                        + (horizontal[0] * (row[x] + row[x + 6] - (2 * center)))
                        + (horizontal[1] * (row[x + 1] + row[x + 5] - (2 * center)))
                        + (horizontal[2] * (row[x + 2] + row[x + 4] - (2 * center)));
                }
            }

            for (var y = 0; y < height; y++)
            {
                var target = original.GetRow(y0 + y);
                var center = filtered.Slice((y + 3) * planeWidth, planeWidth);
                for (var x = 0; x < planeWidth; x++)
                {
                    var unit = sums.Slice(((unitRow * unitCols) + Math.Min(unitCols - 1, x / unitSize)) * UnitSums, UnitSums);
                    long value = center[x];
                    d[0] = filtered[(y * planeWidth) + x] + filtered[((y + 6) * planeWidth) + x] - (2 * value);
                    d[1] = filtered[((y + 1) * planeWidth) + x] + filtered[((y + 5) * planeWidth) + x] - (2 * value);
                    d[2] = filtered[((y + 2) * planeWidth) + x] + filtered[((y + 4) * planeWidth) + x] - (2 * value);
                    Add(unit, d, (128L * target[x]) - value);
                }
            }
        }

        static void Add(Span<long> unit, ReadOnlySpan<long> d, long error)
        {
            unit[0] += d[0] * d[0];
            unit[1] += d[0] * d[1];
            unit[2] += d[0] * d[2];
            unit[3] += d[1] * d[1];
            unit[4] += d[1] * d[2];
            unit[5] += d[2] * d[2];
            unit[6] += d[0] * error;
            unit[7] += d[1] * error;
            unit[8] += d[2] * error;
        }
    }

    // The coefficients of a direction of each unit: the solution of its normal equations, in 1/128, within the ranges of
    // the syntax. A unit whose equations have no single solution keeps the coefficients of no filter
    private static void Solve(int plane, ReadOnlySpan<long> sums, Span<sbyte> units, int pass)
    {
        Span<long> matrix = stackalloc long[9];
        Span<long> right = stackalloc long[3];
        Span<long> column = stackalloc long[9];
        for (var unit = 0; unit < sums.Length / UnitSums; unit++)
        {
            var sum = sums.Slice(unit * UnitSums, UnitSums);
            var coefficients = units.Slice((unit * Av1FrameState.LoopRestorationUnitLength) + 1 + (pass * 3), 3);
            coefficients.Clear();

            // The sums are scaled down together, which leaves the solution as it is, until the determinants hold in 128 bits
            long largest = 0;
            foreach (var value in sum)
            {
                largest = Math.Max(largest, Math.Abs(value));
            }

            var shift = Math.Max(0, 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)largest) - 30);
            matrix[0] = sum[0] >> shift;
            matrix[1] = matrix[3] = sum[1] >> shift;
            matrix[2] = matrix[6] = sum[2] >> shift;
            matrix[4] = sum[3] >> shift;
            matrix[5] = matrix[7] = sum[4] >> shift;
            matrix[8] = sum[5] >> shift;
            right[0] = sum[6] >> shift;
            right[1] = sum[7] >> shift;
            right[2] = sum[8] >> shift;
            var first = plane is 0 ? 0 : 1;
            if (first is 1)
            {
                // The outer taps of a chroma filter are zero: the equations of the two other coefficients
                matrix[0] = 1;
                matrix[1] = matrix[2] = matrix[3] = matrix[6] = 0;
                right[0] = 0;
            }

            var determinant = Determinant(matrix);
            if (determinant == Int128.Zero)
                continue;

            for (var j = first; j < 3; j++)
            {
                matrix.CopyTo(column);
                column[j] = right[0];
                column[3 + j] = right[1];
                column[6 + j] = right[2];

                // Rounded to nearest: the coefficient is 128 times the ratio of the determinants
                var numerator = 128 * Determinant(column);
                var half = Int128.Abs(determinant) / 2;
                var quotient = (numerator >= 0 ? numerator + half : numerator - half) / Int128.Abs(determinant);
                if (determinant < 0)
                    quotient = -quotient;

                coefficients[j] = (sbyte)Int128.Clamp(quotient, Av1Tables.WienerTapsMin[j], Av1Tables.WienerTapsMax[j]);
            }
        }

        static Int128 Determinant(ReadOnlySpan<long> m)
        {
            return ((Int128)m[0] * (((Int128)m[4] * m[8]) - ((Int128)m[5] * m[7])))
                - ((Int128)m[1] * (((Int128)m[3] * m[8]) - ((Int128)m[5] * m[6])))
                + ((Int128)m[2] * (((Int128)m[3] * m[7]) - ((Int128)m[4] * m[6])));
        }
    }

    // The squared error of each unit against the source: as it is, then restored by the filter of the decoder with the
    // coefficients of the unit
    private static void Measure(Av1PostFilter postFilter, Av1FrameBuffer frame, Av1FrameState state, Av1EncoderWorkspace workspace, int plane, Span<long> errors, CancellationToken cancellationToken)
    {
        errors.Clear();
        var header = frame.Header;
        var sequence = frame.Sequence;
        var subX = plane > 0 ? sequence.SubsamplingX : 0;
        var subY = plane > 0 ? sequence.SubsamplingY : 0;
        var planeWidth = (header.UpscaledWidth + subX) >> subX;
        var unitSize = header.LoopRestorationSize[plane];
        var unitRows = state.GetLoopRestorationUnitRows(plane);
        var unitCols = state.GetLoopRestorationUnitCols(plane);
        var units = state.GetLoopRestorationUnits(plane);
        var original = workspace.GetSource(plane);
        var restored = workspace.GetRestoredBlock();
        var scratch = postFilter.RestorationScratch;
        const int Padding = Av1LoopRestoration.Padding;
        Span<sbyte> parameters = stackalloc sbyte[Av1FrameState.LoopRestorationUnitLength];
        var stripeCount = Av1PostFilter.GetRestorationStripeCount(frame, plane);
        for (var stripe = 0; stripe < stripeCount; stripe++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!postFilter.TryGetRestorationStripe(frame, plane, stripe, out var source, out var sourceStride, out var y0, out var height))
                continue;

            var unitRow = Math.Min(unitRows - 1, (((y0 << subY) + 8) >> subY) / unitSize);
            for (var x = 0; x < planeWidth;)
            {
                var unitCol = Math.Min(unitCols - 1, x / unitSize);
                var unitEnd = unitCol == unitCols - 1 ? planeWidth : (unitCol + 1) * unitSize;
                var width = Math.Min(Av1LoopRestoration.MaxBlockWidth, unitEnd - x);
                var unit = (unitRow * unitCols) + unitCol;
                units.Slice(unit * Av1FrameState.LoopRestorationUnitLength, Av1FrameState.LoopRestorationUnitLength).CopyTo(parameters);
                parameters[0] = Av1Constants.RestoreWiener;
                var sourceOffset = (Padding * sourceStride) + Padding + x;
                Av1LoopRestoration.ApplyWiener(source, sourceStride, sourceOffset, restored, width, width, height, parameters, sequence.BitDepth, scratch);
                long before = 0;
                long after = 0;
                for (var y = 0; y < height; y++)
                {
                    var target = original.GetRow(y0 + y).Slice(x, width);
                    var current = source.Slice(sourceOffset + (y * sourceStride), width);
                    var candidate = restored.Slice(y * width, width);
                    for (var i = 0; i < width; i++)
                    {
                        var difference = target[i] - current[i];
                        before += difference * difference;
                        difference = target[i] - candidate[i];
                        after += difference * difference;
                    }
                }

                errors[2 * unit] += before;
                errors[(2 * unit) + 1] += after;
                x += width;
            }
        }
    }
}
