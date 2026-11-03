using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The state of the weighted predictor over one channel (ISO/IEC 18181-1, section H.5.2): four sub-predictors whose
/// weights follow the errors they made on the neighboring samples. Values are kept with three more bits of precision.
/// </summary>
internal sealed class JpegXlWeightedPredictor : IDisposable
{
    private const int ExtraBits = 3;
    private const long Rounding = ((1 << ExtraBits) >> 1) - 1;
    private const int SubPredictorCount = 4;

    private readonly int _width;
    private readonly int _rowLength;

    // The parameters of the header, read once
    private readonly long _p1;
    private readonly long _p2;
    private readonly long _p3a;
    private readonly long _p3b;
    private readonly long _p3c;
    private readonly long _p3d;
    private readonly long _p3e;
    private readonly uint _weight0;
    private readonly uint _weight1;
    private readonly uint _weight2;
    private readonly uint _weight3;

    // Two rows (the current one and the previous one) of width + 2 positions: the error of the prediction and, interleaved
    // (the four of a position are contiguous), the errors of the four sub-predictors
    private readonly PooledBuffer _errorBuffer;
    private readonly PooledBuffer _subErrorBuffer;

    // The arrays of the buffers, which do not change until they are returned: the spans are read for every sample
    private byte[]? _errors;
    private byte[]? _subErrors;
    private readonly int _errorCount;
    private long _prediction;
    private long _prediction0;
    private long _prediction1;
    private long _prediction2;
    private long _prediction3;

    public JpegXlWeightedPredictor(JpegXlWeightedPredictorHeader header, int width, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(header);
        _width = width;
        _rowLength = width + 2;
        _p1 = header.P1;
        _p2 = header.P2;
        _p3a = header.P3A;
        _p3b = header.P3B;
        _p3c = header.P3C;
        _p3d = header.P3D;
        _p3e = header.P3E;
        _weight0 = header.Weights[0];
        _weight1 = header.Weights[1];
        _weight2 = header.Weights[2];
        _weight3 = header.Weights[3];

        var planeLength = ((long)width + 2) * 2;
        _errorBuffer = Rent(scope, planeLength * sizeof(int));
        try
        {
            _subErrorBuffer = Rent(scope, planeLength * SubPredictorCount * sizeof(uint));
        }
        catch
        {
            _errorBuffer.Dispose();
            throw;
        }

        _errorCount = (int)planeLength;
        _errors = _errorBuffer.RawBuffer;
        _subErrors = _subErrorBuffer.RawBuffer;

        static PooledBuffer Rent(AllocationScope scope, long bytes)
        {
            if (bytes > CheckedSizes.MaxBufferLength)
                throw CheckedSizes.CreateOverflowException(scope.Limits);

            return scope.Rent((int)bytes, AllocationKind.DecoderState, clear: true);
        }
    }

    // The arrays hold at least the values they were rented for
    private Span<int> Errors
    {
        get
        {
            var array = _errors ?? ThrowDisposed();
            return unsafe(MemoryMarshal.CreateSpan(ref Unsafe.As<byte, int>(ref MemoryMarshal.GetArrayDataReference(array)), _errorCount));
        }
    }

    private Span<uint> SubErrors
    {
        get
        {
            var array = _subErrors ?? ThrowDisposed();
            return unsafe(MemoryMarshal.CreateSpan(ref Unsafe.As<byte, uint>(ref MemoryMarshal.GetArrayDataReference(array)), _errorCount * SubPredictorCount));
        }
    }

    // 2^24 / (i + 1)
    private static ReadOnlySpan<uint> Reciprocals =>
    [
        16777216, 8388608, 5592405, 4194304, 3355443, 2796202, 2396745, 2097152,
        1864135, 1677721, 1525201, 1398101, 1290555, 1198372, 1118481, 1048576,
        986895, 932067, 883011, 838860, 798915, 762600, 729444, 699050,
        671088, 645277, 621378, 599186, 578524, 559240, 541200, 524288,
        508400, 493447, 479349, 466033, 453438, 441505, 430185, 419430,
        409200, 399457, 390167, 381300, 372827, 364722, 356962, 349525,
        342392, 335544, 328965, 322638, 316551, 310689, 305040, 299593,
        294337, 289262, 284359, 279620, 275036, 270600, 266305, 262144,
    ];

    /// <summary>Predicts the sample at (<paramref name="x"/>, <paramref name="y"/>) from its neighbors (already replaced at the edges of the channel).</summary>
    /// <param name="x">The column.</param>
    /// <param name="y">The row.</param>
    /// <param name="north">The sample above.</param>
    /// <param name="west">The sample on the left.</param>
    /// <param name="northEast">The sample above on the right.</param>
    /// <param name="northWest">The sample above on the left.</param>
    /// <param name="northNorth">The sample two rows above.</param>
    /// <param name="maxError">Receives the property of the predictor: the neighboring error of largest magnitude.</param>
    /// <returns>The prediction.</returns>
    public long Predict(int x, int y, long north, long west, long northEast, long northWest, long northNorth, out int maxError)
    {
        var rowLength = _rowLength;
        var previousRow = (y & 1) != 0 ? rowLength : 0;
        var currentRow = rowLength - previousRow;
        var positionN = previousRow + x;
        var positionNE = x < _width - 1 ? positionN + 1 : positionN;
        var positionNW = x > 0 ? positionN - 1 : positionN;
        var errors = Errors;

        // The errors of the four sub-predictors at the three neighbors above, added lane by lane (wrapping, as uint additions do)
        var subErrors = SubErrors;
        var sums = LoadSubErrors(subErrors, positionN) + LoadSubErrors(subErrors, positionNE) + LoadSubErrors(subErrors, positionNW);
        var weight0 = GetErrorWeight(sums.GetElement(0), _weight0);
        var weight1 = GetErrorWeight(sums.GetElement(1), _weight1);
        var weight2 = GetErrorWeight(sums.GetElement(2), _weight2);
        var weight3 = GetErrorWeight(sums.GetElement(3), _weight3);

        north <<= ExtraBits;
        west <<= ExtraBits;
        northEast <<= ExtraBits;
        northWest <<= ExtraBits;
        northNorth <<= ExtraBits;

        long errorW = x == 0 ? 0 : errors[currentRow + x - 1];
        long errorN = errors[positionN];
        long errorNW = errors[positionNW];
        long errorNE = errors[positionNE];
        var sumWN = errorN + errorW;

        // The errors are 32-bit values: their magnitudes are computed without the overflow check of Math.Abs
        long largest = errorW;
        if (Magnitude(errorN) > Magnitude(largest))
        {
            largest = errorN;
        }

        if (Magnitude(errorNW) > Magnitude(largest))
        {
            largest = errorNW;
        }

        if (Magnitude(errorNE) > Magnitude(largest))
        {
            largest = errorNE;
        }

        maxError = (int)largest;

        var prediction0 = west + northEast - north;
        var prediction1 = north - (((sumWN + errorNE) * _p1) >> 5);
        var prediction2 = west - (((sumWN + errorNW) * _p2) >> 5);
        var prediction3 = north - (((errorNW * _p3a) + (errorN * _p3b) + (errorNE * _p3c) + ((northNorth - north) * _p3d) + ((northWest - west) * _p3e)) >> 5);
        _prediction0 = prediction0;
        _prediction1 = prediction1;
        _prediction2 = prediction2;
        _prediction3 = prediction3;

        // The weighted average: the weights are reduced to 4 bits of precision above their leading bit
        var shift = BitOperations.Log2(weight0 + weight1 + weight2 + weight3) - 4;
        weight0 >>= shift;
        weight1 >>= shift;
        weight2 >>= shift;
        weight3 >>= shift;
        var weightSum = weight0 + weight1 + weight2 + weight3;
        var total = (long)(weightSum >> 1) - 1 + (prediction0 * weight0) + (prediction1 * weight1) + (prediction2 * weight2) + (prediction3 * weight3);
        // The sum of the reduced weights is 13 to 31
        var prediction = (total * Reciprocals[((int)weightSum - 1) & 63]) >> 24;

        // When the errors of the neighbors do not all have the same sign, the prediction is kept; otherwise it is clamped to the neighbors
        if (((errorN ^ errorW) | (errorN ^ errorNW)) <= 0)
        {
            var max = Math.Max(west, Math.Max(northEast, north));
            var min = Math.Min(west, Math.Min(northEast, north));
            prediction = Math.Max(min, Math.Min(max, prediction));
        }

        _prediction = prediction;
        return (prediction + Rounding) >> ExtraBits;
    }

    /// <summary>
    /// Predicts the samples of a row one after the other, each from its neighbors and from the errors of the samples before
    /// it, with the same results as <see cref="Predict"/> then <see cref="Update"/> for each sample: the sink receives each
    /// prediction with its property and gives the sample back.
    /// </summary>
    /// <remarks>
    /// The errors of the four sub-predictors at the three neighbors above are summed from the previous row as it was
    /// recorded, and those of the two samples on the left, which <see cref="Update"/> adds to the previous row, are kept in
    /// registers: the previous row is not written.
    /// </remarks>
    /// <param name="y">The row.</param>
    /// <param name="above">The row above (empty for the first row).</param>
    /// <param name="twoAbove">The row two above (the row above for the second row).</param>
    /// <param name="sink">Receives the predictions and returns the samples.</param>
    public void PredictRow<TSink>(int y, ReadOnlySpan<int> above, ReadOnlySpan<int> twoAbove, ref TSink sink)
        where TSink : struct, IRowSink, allows ref struct
    {
        var width = _width;
        var errors = GetRowErrors(y);
        var parameters = GetParameters();
        var state = default(RowState);
        if (y == 0 || width < 3)
        {
            for (var x = 0; x < width; x++)
            {
                PredictEdge(ref sink, in parameters, ref state, x, y, above, twoAbove, errors);
            }

            return;
        }

        PredictEdge(ref sink, in parameters, ref state, 0, y, above, twoAbove, errors);
        var last = width - 1;
        var aboveRow = above[..width];
        var topTopRow = twoAbove[..width];
        var previousErrors = errors.Previous;
        var currentErrors = errors.Current;
        var currentSubErrors = errors.CurrentSub;
        ref var previousSubErrors = ref unsafe(MemoryMarshal.GetReference(errors.PreviousSub));
        Vector128<uint> subN;
        Vector128<uint> subNE;
        unsafe
        {
            // The rows of sub-errors hold four values per column: the reads stay below the last column
            subN = Vector128.LoadUnsafe(ref previousSubErrors, 0);
            subNE = Vector128.LoadUnsafe(ref previousSubErrors, SubPredictorCount);
        }

        var errorW = state.ErrorW;
        var west = state.West;
        var w0 = state.W0;
        var w1 = state.W1;
        var w2 = state.W2;
        var w3 = state.W3;
        var ww0 = state.WW0;
        var ww1 = state.WW1;
        var ww2 = state.WW2;
        var ww3 = state.WW3;
        for (var x = 1; x < last; x++)
        {
            var subNW = subN;
            subN = subNE;
            unsafe
            {
                subNE = Vector128.LoadUnsafe(ref previousSubErrors, (nuint)((x + 1) * SubPredictorCount));
            }

            // The errors above and those of the sample two on the left are known before the sample on the left
            var above3 = subNW + subN + subNE;
            long errorN = previousErrors[x];
            long errorNW = previousErrors[x - 1];
            long errorNE = previousErrors[x + 1];
            var prediction = PredictSample(
                in parameters,
                above3.GetElement(0) + ww0 + w0,
                above3.GetElement(1) + ww1 + w1,
                above3.GetElement(2) + ww2 + w2,
                above3.GetElement(3) + ww3 + w3,
                aboveRow[x],
                west,
                aboveRow[x + 1],
                aboveRow[x - 1],
                topTopRow[x],
                errorW,
                errorN,
                errorNW,
                errorNE,
                out var prediction0,
                out var prediction1,
                out var prediction2,
                out var prediction3);
            var sample = sink.Next(x, Round(prediction), GetMaxError(errorW, errorN, errorNW, errorNE));
            ww0 = w0;
            ww1 = w1;
            ww2 = w2;
            ww3 = w3;
            errorW = Record(prediction, prediction0, prediction1, prediction2, prediction3, sample, x, currentErrors, currentSubErrors, out w0, out w1, out w2, out w3);
            west = sample;
        }

        state = new RowState(w0, w1, w2, w3, ww0, ww1, ww2, ww3, errorW, west);
        PredictEdge(ref sink, in parameters, ref state, last, y, above, twoAbove, errors);
    }

    /// <summary>Gets the parameters of the header, which a loop over the samples keeps in registers.</summary>
    internal Parameters GetParameters() => new(this);

    /// <summary>Gets the errors recorded for the row above <paramref name="y"/> and those of the row itself.</summary>
    internal RowErrors GetRowErrors(int y)
    {
        var width = _width;
        var rowLength = _rowLength;
        var previousRow = (y & 1) != 0 ? rowLength : 0;
        var currentRow = rowLength - previousRow;
        var errors = Errors;
        var subErrors = SubErrors;
        return new RowErrors(
            errors.Slice(previousRow, width),
            errors.Slice(currentRow, width),
            subErrors.Slice(previousRow * SubPredictorCount, width * SubPredictorCount),
            subErrors.Slice(currentRow * SubPredictorCount, width * SubPredictorCount));
    }

    /// <summary>
    /// Predicts a sample of a row predicted by <see cref="PredictRow"/>, or by a loop that does the same, that is on the first
    /// row, or in the first or the last column.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void PredictEdge<TSink>(ref TSink sink, in Parameters parameters, ref RowState state, int x, int y, ReadOnlySpan<int> above, ReadOnlySpan<int> twoAbove, in RowErrors errors)
        where TSink : struct, IRowSink, allows ref struct
    {
        // The neighbors that are outside the channel are replaced as in the general loop of the Modular decoder
        var width = errors.Current.Length;
        long north;
        long west;
        long northEast;
        long northWest;
        long northNorth;
        if (y == 0)
        {
            west = x > 0 ? state.West : 0;
            north = west;
            northEast = west;
            northWest = west;
            northNorth = west;
        }
        else
        {
            north = above[x];
            west = x > 0 ? state.West : north;
            northEast = x + 1 < width ? above[x + 1] : north;
            northWest = x > 0 ? above[x - 1] : west;
            northNorth = twoAbove[x];
        }

        // The errors at the neighbors above (those at the edge are the error above), with the errors that the two samples
        // on the left add to the sample above and to the one above on the left
        var positionNE = x + 1 < width ? x + 1 : x;
        var positionNW = x > 0 ? x - 1 : x;
        var previousSub = errors.PreviousSub;
        var sums = LoadSubErrors(previousSub, x) + LoadSubErrors(previousSub, positionNE) + LoadSubErrors(previousSub, positionNW);
        var left = Vector128.Create(state.W0, state.W1, state.W2, state.W3);
        sums += left;
        if (positionNE == x)
        {
            sums += left;
        }

        if (positionNW != x)
        {
            sums += Vector128.Create(state.WW0, state.WW1, state.WW2, state.WW3);
        }

        var previousErrors = errors.Previous;
        long errorN = previousErrors[x];
        long errorNW = previousErrors[positionNW];
        long errorNE = previousErrors[positionNE];
        var prediction = PredictSample(in parameters, sums.GetElement(0), sums.GetElement(1), sums.GetElement(2), sums.GetElement(3), north, west, northEast, northWest, northNorth, state.ErrorW, errorN, errorNW, errorNE, out var prediction0, out var prediction1, out var prediction2, out var prediction3);
        var sample = sink.Next(x, Round(prediction), GetMaxError(state.ErrorW, errorN, errorNW, errorNE));
        var error = Record(prediction, prediction0, prediction1, prediction2, prediction3, sample, x, errors.Current, errors.CurrentSub, out var w0, out var w1, out var w2, out var w3);
        state = new RowState(w0, w1, w2, w3, state.W0, state.W1, state.W2, state.W3, error, sample);
    }

    /// <summary>
    /// Computes the prediction of a sample, with three more bits of precision, from the sums of the errors of each
    /// sub-predictor at its neighbors and from its neighbors (already replaced at the edges of the channel).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long PredictSample(
        in Parameters parameters,
        uint sum0,
        uint sum1,
        uint sum2,
        uint sum3,
        long north,
        long west,
        long northEast,
        long northWest,
        long northNorth,
        long errorW,
        long errorN,
        long errorNW,
        long errorNE,
        out long prediction0,
        out long prediction1,
        out long prediction2,
        out long prediction3)
    {
        var weight0 = GetErrorWeight(sum0, parameters.Weight0);
        var weight1 = GetErrorWeight(sum1, parameters.Weight1);
        var weight2 = GetErrorWeight(sum2, parameters.Weight2);
        var weight3 = GetErrorWeight(sum3, parameters.Weight3);

        north <<= ExtraBits;
        west <<= ExtraBits;
        northEast <<= ExtraBits;
        northWest <<= ExtraBits;
        northNorth <<= ExtraBits;
        var sumWN = errorN + errorW;
        prediction0 = west + northEast - north;
        prediction1 = north - (((sumWN + errorNE) * parameters.P1) >> 5);
        prediction2 = west - (((sumWN + errorNW) * parameters.P2) >> 5);
        prediction3 = north - (((errorNW * parameters.P3A) + (errorN * parameters.P3B) + (errorNE * parameters.P3C) + ((northNorth - north) * parameters.P3D) + ((northWest - west) * parameters.P3E)) >> 5);

        // The weights are reduced to 4 bits of precision above their leading bit, then the products are summed in pairs: the
        // sums are the same in any order
        var shift = BitOperations.Log2((weight0 + weight1) + (weight2 + weight3)) - 4;
        weight0 >>= shift;
        weight1 >>= shift;
        weight2 >>= shift;
        weight3 >>= shift;
        var weightSum = (weight0 + weight1) + (weight2 + weight3);
        var total = ((long)(weightSum >> 1) - 1) + ((prediction0 * weight0) + (prediction1 * weight1)) + ((prediction2 * weight2) + (prediction3 * weight3));

        // The sum of the reduced weights is 13 to 31
        var prediction = (total * Reciprocals[((int)weightSum - 1) & 63]) >> 24;

        // When the errors of the neighbors do not all have the same sign, the prediction is kept; otherwise it is clamped to the
        // neighbors. The condition and the comparisons are not predictable: they select with masks (the errors are 32-bit
        // values: the condition is that their combination minus one is negative)
        if (((errorN ^ errorW) | (errorN ^ errorNW)) <= 0)
        {
            var max = Max(west, Max(northEast, north));
            var min = Min(west, Min(northEast, north));
            prediction = Max(min, Min(max, prediction));
        }

        return prediction;
    }

    /// <summary>Returns -1 when <paramref name="value"/> is less than <paramref name="other"/>, 0 otherwise, for any values.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long LessThan(long value, long other)
    {
        // The sign of the difference, corrected when it overflows (the operands have different signs and the difference has
        // the sign of the subtrahend)
        var difference = value - other;
        return (difference ^ ((value ^ other) & (difference ^ value))) >> 63;
    }

    /// <summary>Returns <paramref name="ifSet"/> when <paramref name="mask"/> is -1, <paramref name="otherwise"/> when it is 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Select(long mask, long ifSet, long otherwise) => (ifSet & mask) | (otherwise & ~mask);

    /// <summary>Returns the result of Math.Min, without a branch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Min(long value1, long value2) => Select(LessThan(value2, value1), value2, value1);

    /// <summary>Returns the result of Math.Max, without a branch.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Max(long value1, long value2) => Select(LessThan(value1, value2), value2, value1);

    /// <summary>Returns a prediction of <see cref="PredictSample"/> without its extra bits.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long Round(long prediction) => (prediction + Rounding) >> ExtraBits;

    /// <summary>Returns the property of the predictor: the error of largest magnitude at the neighbors, the first one on a tie.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int GetMaxError(long errorW, long errorN, long errorNW, long errorNE)
    {
        // The errors above are compared first: the error on the left, which is the last one known, is compared once. The
        // comparisons are not predictable: they select with masks
        var largest = Select(LessThan(Magnitude(errorN), Magnitude(errorNW)), errorNW, errorN);
        largest = Select(LessThan(Magnitude(largest), Magnitude(errorNE)), errorNE, largest);
        return (int)Select(LessThan(Magnitude(errorW), Magnitude(largest)), largest, errorW);
    }

    /// <summary>Records the errors of a sample once it is known, and returns its error, which the next sample reads.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static long Record(long prediction, long prediction0, long prediction1, long prediction2, long prediction3, int sample, int x, Span<int> currentErrors, Span<uint> currentSubErrors, out uint subError0, out uint subError1, out uint subError2, out uint subError3)
    {
        var scaled = (long)sample << ExtraBits;

        // The error is recorded as a 32-bit value, which is what the next samples read
        var error = (int)(prediction - scaled);
        currentErrors[x] = error;
        subError0 = GetSubError(prediction0, scaled);
        subError1 = GetSubError(prediction1, scaled);
        subError2 = GetSubError(prediction2, scaled);
        subError3 = GetSubError(prediction3, scaled);
        Vector128.Create(subError0, subError1, subError2, subError3).CopyTo(currentSubErrors.Slice(x * SubPredictorCount, SubPredictorCount));
        return error;
    }

    /// <summary>Receives the predictions of a row (<see cref="PredictRow"/>) and gives the samples back.</summary>
    internal interface IRowSink
    {
        /// <summary>Returns the sample of a column from its prediction and the property of the predictor.</summary>
        int Next(int x, long prediction, int maxError);
    }

    /// <summary>The errors recorded for the row above (read) and for the row being predicted (written).</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly ref struct RowErrors
    {
        public RowErrors(ReadOnlySpan<int> previous, Span<int> current, ReadOnlySpan<uint> previousSub, Span<uint> currentSub)
        {
            Previous = previous;
            Current = current;
            PreviousSub = previousSub;
            CurrentSub = currentSub;
        }

        /// <summary>Gets the errors of the row above.</summary>
        public ReadOnlySpan<int> Previous { get; }

        /// <summary>Gets the errors of the row.</summary>
        public Span<int> Current { get; }

        /// <summary>Gets the errors of the four sub-predictors of each column of the row above.</summary>
        public ReadOnlySpan<uint> PreviousSub { get; }

        /// <summary>Gets the errors of the four sub-predictors of each column of the row.</summary>
        public Span<uint> CurrentSub { get; }
    }

    /// <summary>The errors of the sub-predictors at the two samples on the left, the error of the sample on the left and its value.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly record struct RowState(uint W0, uint W1, uint W2, uint W3, uint WW0, uint WW1, uint WW2, uint WW3, long ErrorW, long West);

    /// <summary>The parameters of the header.</summary>
    [StructLayout(LayoutKind.Auto)]
    internal readonly struct Parameters
    {
        public readonly long P1;
        public readonly long P2;
        public readonly long P3A;
        public readonly long P3B;
        public readonly long P3C;
        public readonly long P3D;
        public readonly long P3E;
        public readonly uint Weight0;
        public readonly uint Weight1;
        public readonly uint Weight2;
        public readonly uint Weight3;

        public Parameters(JpegXlWeightedPredictor predictor)
        {
            P1 = predictor._p1;
            P2 = predictor._p2;
            P3A = predictor._p3a;
            P3B = predictor._p3b;
            P3C = predictor._p3c;
            P3D = predictor._p3d;
            P3E = predictor._p3e;
            Weight0 = predictor._weight0;
            Weight1 = predictor._weight1;
            Weight2 = predictor._weight2;
            Weight3 = predictor._weight3;
        }
    }

    /// <summary>Records the errors of the last prediction once the sample is known.</summary>
    public void Update(int value, int x, int y)
    {
        var rowLength = _rowLength;
        var previousRow = (y & 1) != 0 ? rowLength : 0;
        var currentRow = rowLength - previousRow;
        var scaled = (long)value << ExtraBits;
        Errors[currentRow + x] = (int)(_prediction - scaled);
        var errors = Vector128.Create(GetSubError(_prediction0, scaled), GetSubError(_prediction1, scaled), GetSubError(_prediction2, scaled), GetSubError(_prediction3, scaled));
        var subErrors = SubErrors;
        errors.CopyTo(subErrors.Slice((currentRow + x) * SubPredictorCount, SubPredictorCount));

        // The errors also count for the sample on the right of the one above
        var right = subErrors.Slice((previousRow + x + 1) * SubPredictorCount, SubPredictorCount);
        (Vector128.Create<uint>(right) + errors).CopyTo(right);
    }

    public void Dispose()
    {
        _errors = null;
        _subErrors = null;
        _errorBuffer.Dispose();
        _subErrorBuffer.Dispose();
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static byte[] ThrowDisposed() => throw new ObjectDisposedException(nameof(JpegXlWeightedPredictor));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<uint> LoadSubErrors(ReadOnlySpan<uint> subErrors, int position) => Vector128.Create(subErrors.Slice(position * SubPredictorCount, SubPredictorCount));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Magnitude(long value) => (value ^ (value >> 63)) - (value >> 63);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetSubError(long prediction, long scaled)
    {
        // The predictions and the samples are far from the limits of a 64-bit value: the magnitude is computed without the
        // branch and the overflow check of Math.Abs (the sign of an error is not predictable)
        return (uint)((Magnitude(prediction - scaled) + Rounding) >> ExtraBits);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetErrorWeight(uint error, uint maxWeight)
    {
        // The error shifted right is below 64: it is below 2^(shift + 6)
        // The shift is computed without a branch: the errors are not predictable
        var excess = BitOperations.Log2((ulong)error + 1) - 5;
        var shift = excess & ~(excess >> 31);
        return 4 + ((maxWeight * Reciprocals[(int)(error >> shift) & 63]) >> shift);
    }
}
