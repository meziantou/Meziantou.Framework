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
    private static uint GetSubError(long prediction, long scaled) => (uint)((Math.Abs(prediction - scaled) + Rounding) >> ExtraBits);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint GetErrorWeight(uint error, uint maxWeight)
    {
        // The error shifted right is below 64: it is below 2^(shift + 6)
        var shift = Math.Max(0, BitOperations.Log2((ulong)error + 1) - 5);
        return 4 + ((maxWeight * Reciprocals[(int)(error >> shift) & 63]) >> shift);
    }
}
