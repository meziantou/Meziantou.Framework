using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;

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

            // The samples of a unit are the columns from its start to the start of the next one, and to the end of the
            // plane for the last one
            var unitRow = Math.Min(unitRows - 1, (((y0 << subY) + 8) >> subY) / unitSize);
            if (!vertical)
            {
                for (var y = 0; y < height; y++)
                {
                    var row = source.Slice(((y + Padding) * sourceStride) + Padding - 3, planeWidth + 6);
                    var target = original.GetRow(y0 + y);
                    for (var unitCol = 0; unitCol < unitCols; unitCol++)
                    {
                        var x0 = Math.Min(planeWidth, unitCol * unitSize);
                        var x1 = unitCol == unitCols - 1 ? planeWidth : Math.Min(planeWidth, x0 + unitSize);
                        var unit = sums.Slice(((unitRow * unitCols) + unitCol) * UnitSums, UnitSums);
                        AccumulateHorizontal(row[x0..(x1 + 6)], target[x0..x1], unit);
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
                for (var unitCol = 0; unitCol < unitCols; unitCol++)
                {
                    var x0 = Math.Min(planeWidth, unitCol * unitSize);
                    var x1 = unitCol == unitCols - 1 ? planeWidth : Math.Min(planeWidth, x0 + unitSize);
                    var horizontal = units.Slice((((unitRow * unitCols) + unitCol) * Av1FrameState.LoopRestorationUnitLength) + 4, 3);
                    int h0 = horizontal[0];
                    int h1 = horizontal[1];
                    int h2 = horizontal[2];
                    for (var x = x0; x < x1; x++)
                    {
                        var center = row[x + 3];
                        output[x] = (128 * center)
                            + (h0 * (row[x] + row[x + 6] - (2 * center)))
                            + (h1 * (row[x + 1] + row[x + 5] - (2 * center)))
                            + (h2 * (row[x + 2] + row[x + 4] - (2 * center)));
                    }
                }
            }

            for (var y = 0; y < height; y++)
            {
                var target = original.GetRow(y0 + y);
                var rows = filtered[(y * planeWidth)..];
                for (var unitCol = 0; unitCol < unitCols; unitCol++)
                {
                    var x0 = Math.Min(planeWidth, unitCol * unitSize);
                    var x1 = unitCol == unitCols - 1 ? planeWidth : Math.Min(planeWidth, x0 + unitSize);
                    var unit = sums.Slice(((unitRow * unitCols) + unitCol) * UnitSums, UnitSums);
                    AccumulateVertical(rows[x0..], planeWidth, target[x0..x1], unit);
                }
            }
        }
    }

    /// <summary>
    /// Adds to the 9 sums of <paramref name="unit"/> the products of the differences of a row of samples, for the
    /// horizontal coefficients: <paramref name="row"/> holds 3 samples before and 3 after the samples of
    /// <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// With vectors, 4 samples at once: the differences are 32-bit and their products are 64-bit (widening
    /// multiply-accumulate), the products of the scalar code; the sums of integers do not depend on their order.
    /// </remarks>
    internal static void AccumulateHorizontal(ReadOnlySpan<ushort> row, ReadOnlySpan<ushort> target, Span<long> unit)
    {
        if (AdvSimd.IsSupported)
        {
            AccumulateHorizontalVector<Av1AdvSimd>(row, target, unit);
        }
        else if (Av1PortableSimd.IsAccelerated)
        {
            AccumulateHorizontalVector<Av1PortableSimd>(row, target, unit);
        }
        else
        {
            AccumulateHorizontalScalar(row, target, unit);
        }
    }

    /// <summary><see cref="AccumulateHorizontal"/> with the vector operations of <typeparamref name="TSimd"/>.</summary>
    internal static void AccumulateHorizontalVector<TSimd>(ReadOnlySpan<ushort> row, ReadOnlySpan<ushort> target, Span<long> unit)
        where TSimd : struct, IAv1Simd
    {
        var width = target.Length;
        row = row[..(width + 6)];
        var x = 0;
        if (width >= 4)
        {
            var sums = new ProductSums();
            for (; x <= width - 4; x += 4)
            {
                var center = Load(row, x + 3);
                var twice = center + center;
                var d0 = Load(row, x) + Load(row, x + 6) - twice;
                var d1 = Load(row, x + 1) + Load(row, x + 5) - twice;
                var d2 = Load(row, x + 2) + Load(row, x + 4) - twice;
                sums.Add<TSimd>(d0, d1, d2, Load(target, x) - center);
            }

            sums.AddTo(unit);
        }

        Span<long> d = stackalloc long[3];
        for (; x < width; x++)
        {
            long center = row[x + 3];
            d[0] = row[x] + row[x + 6] - (2 * center);
            d[1] = row[x + 1] + row[x + 5] - (2 * center);
            d[2] = row[x + 2] + row[x + 4] - (2 * center);
            Add(unit, d, target[x] - center);
        }

        static Vector128<int> Load(ReadOnlySpan<ushort> samples, int index) => Vector128.WidenLower(Vector64.Create(samples.Slice(index, 4)).ToVector128()).AsInt32();
    }

    /// <summary>The scalar reference of <see cref="AccumulateHorizontal"/>.</summary>
    internal static void AccumulateHorizontalScalar(ReadOnlySpan<ushort> row, ReadOnlySpan<ushort> target, Span<long> unit)
    {
        Span<long> d = stackalloc long[3];
        for (var x = 0; x < target.Length; x++)
        {
            long center = row[x + 3];
            d[0] = row[x] + row[x + 6] - (2 * center);
            d[1] = row[x + 1] + row[x + 5] - (2 * center);
            d[2] = row[x + 2] + row[x + 4] - (2 * center);
            Add(unit, d, target[x] - center);
        }
    }

    /// <summary>
    /// Adds to the 9 sums of <paramref name="unit"/> the products of the differences of the horizontally filtered rows, for
    /// the vertical coefficients: <paramref name="rows"/> holds 7 rows of <paramref name="stride"/> values, the target row
    /// being the fourth one, from the column of the first sample of <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// With vectors, 4 samples at once: the values and their differences (below 2^24 for 12-bit samples) are 32-bit and
    /// their products are 64-bit, the products of the scalar code.
    /// </remarks>
    internal static void AccumulateVertical(ReadOnlySpan<int> rows, int stride, ReadOnlySpan<ushort> target, Span<long> unit)
    {
        if (AdvSimd.IsSupported)
        {
            AccumulateVerticalVector<Av1AdvSimd>(rows, stride, target, unit);
        }
        else if (Av1PortableSimd.IsAccelerated)
        {
            AccumulateVerticalVector<Av1PortableSimd>(rows, stride, target, unit);
        }
        else
        {
            AccumulateVerticalScalar(rows, stride, target, unit);
        }
    }

    /// <summary><see cref="AccumulateVertical"/> with the vector operations of <typeparamref name="TSimd"/>.</summary>
    internal static void AccumulateVerticalVector<TSimd>(ReadOnlySpan<int> rows, int stride, ReadOnlySpan<ushort> target, Span<long> unit)
        where TSimd : struct, IAv1Simd
    {
        var width = target.Length;
        var x = 0;
        if (width >= 4)
        {
            var sums = new ProductSums();
            for (; x <= width - 4; x += 4)
            {
                var value = Vector128.Create(rows.Slice((3 * stride) + x, 4));
                var twice = value + value;
                var d0 = Vector128.Create(rows.Slice(x, 4)) + Vector128.Create(rows.Slice((6 * stride) + x, 4)) - twice;
                var d1 = Vector128.Create(rows.Slice(stride + x, 4)) + Vector128.Create(rows.Slice((5 * stride) + x, 4)) - twice;
                var d2 = Vector128.Create(rows.Slice((2 * stride) + x, 4)) + Vector128.Create(rows.Slice((4 * stride) + x, 4)) - twice;
                var targets = Vector128.WidenLower(Vector64.Create(target.Slice(x, 4)).ToVector128()).AsInt32();
                sums.Add<TSimd>(d0, d1, d2, (targets * 128) - value);
            }

            sums.AddTo(unit);
        }

        AccumulateVerticalScalar(rows[x..], stride, target[x..], unit);
    }

    /// <summary>The scalar reference of <see cref="AccumulateVertical"/>.</summary>
    internal static void AccumulateVerticalScalar(ReadOnlySpan<int> rows, int stride, ReadOnlySpan<ushort> target, Span<long> unit)
    {
        Span<long> d = stackalloc long[3];
        for (var x = 0; x < target.Length; x++)
        {
            long value = rows[(3 * stride) + x];
            d[0] = rows[x] + rows[(6 * stride) + x] - (2 * value);
            d[1] = rows[stride + x] + rows[(5 * stride) + x] - (2 * value);
            d[2] = rows[(2 * stride) + x] + rows[(4 * stride) + x] - (2 * value);
            Add(unit, d, (128L * target[x]) - value);
        }
    }

    private static void Add(Span<long> unit, ReadOnlySpan<long> d, long error)
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

    // The 9 sums of products of Add, for 4 samples at once: a 64-bit sum for each of the lower and the upper 2 lanes
    [StructLayout(LayoutKind.Auto)]
    private struct ProductSums
    {
        private Vector128<long> _s00Low;
        private Vector128<long> _s00High;
        private Vector128<long> _s01Low;
        private Vector128<long> _s01High;
        private Vector128<long> _s02Low;
        private Vector128<long> _s02High;
        private Vector128<long> _s11Low;
        private Vector128<long> _s11High;
        private Vector128<long> _s12Low;
        private Vector128<long> _s12High;
        private Vector128<long> _s22Low;
        private Vector128<long> _s22High;
        private Vector128<long> _s0eLow;
        private Vector128<long> _s0eHigh;
        private Vector128<long> _s1eLow;
        private Vector128<long> _s1eHigh;
        private Vector128<long> _s2eLow;
        private Vector128<long> _s2eHigh;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add<TSimd>(Vector128<int> d0, Vector128<int> d1, Vector128<int> d2, Vector128<int> error)
            where TSimd : struct, IAv1Simd
        {
            _s00Low = TSimd.MultiplyWideningLowerAndAdd(_s00Low, d0, d0);
            _s00High = TSimd.MultiplyWideningUpperAndAdd(_s00High, d0, d0);
            _s01Low = TSimd.MultiplyWideningLowerAndAdd(_s01Low, d0, d1);
            _s01High = TSimd.MultiplyWideningUpperAndAdd(_s01High, d0, d1);
            _s02Low = TSimd.MultiplyWideningLowerAndAdd(_s02Low, d0, d2);
            _s02High = TSimd.MultiplyWideningUpperAndAdd(_s02High, d0, d2);
            _s11Low = TSimd.MultiplyWideningLowerAndAdd(_s11Low, d1, d1);
            _s11High = TSimd.MultiplyWideningUpperAndAdd(_s11High, d1, d1);
            _s12Low = TSimd.MultiplyWideningLowerAndAdd(_s12Low, d1, d2);
            _s12High = TSimd.MultiplyWideningUpperAndAdd(_s12High, d1, d2);
            _s22Low = TSimd.MultiplyWideningLowerAndAdd(_s22Low, d2, d2);
            _s22High = TSimd.MultiplyWideningUpperAndAdd(_s22High, d2, d2);
            _s0eLow = TSimd.MultiplyWideningLowerAndAdd(_s0eLow, d0, error);
            _s0eHigh = TSimd.MultiplyWideningUpperAndAdd(_s0eHigh, d0, error);
            _s1eLow = TSimd.MultiplyWideningLowerAndAdd(_s1eLow, d1, error);
            _s1eHigh = TSimd.MultiplyWideningUpperAndAdd(_s1eHigh, d1, error);
            _s2eLow = TSimd.MultiplyWideningLowerAndAdd(_s2eLow, d2, error);
            _s2eHigh = TSimd.MultiplyWideningUpperAndAdd(_s2eHigh, d2, error);
        }

        public readonly void AddTo(Span<long> unit)
        {
            unit[0] += Vector128.Sum(_s00Low + _s00High);
            unit[1] += Vector128.Sum(_s01Low + _s01High);
            unit[2] += Vector128.Sum(_s02Low + _s02High);
            unit[3] += Vector128.Sum(_s11Low + _s11High);
            unit[4] += Vector128.Sum(_s12Low + _s12High);
            unit[5] += Vector128.Sum(_s22Low + _s22High);
            unit[6] += Vector128.Sum(_s0eLow + _s0eHigh);
            unit[7] += Vector128.Sum(_s1eLow + _s1eHigh);
            unit[8] += Vector128.Sum(_s2eLow + _s2eHigh);
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
