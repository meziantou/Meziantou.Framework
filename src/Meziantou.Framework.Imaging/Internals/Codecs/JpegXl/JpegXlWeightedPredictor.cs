using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The state of the weighted predictor over one channel (ISO/IEC 18181-1, section H.5.2): four sub-predictors whose
/// weights follow the errors they made on the neighboring samples. Values are kept with three more bits of precision.
/// </summary>
internal sealed class JpegXlWeightedPredictor : IDisposable
{
    private const int ExtraBits = 3;
    private const long Rounding = ((1 << ExtraBits) >> 1) - 1;

    private readonly JpegXlWeightedPredictorHeader _header;
    private readonly int _width;
    private readonly Av1PooledArray<int> _errors;
    private readonly Av1PooledArray<uint> _subErrors;
    private long _prediction;
    private long _prediction0;
    private long _prediction1;
    private long _prediction2;
    private long _prediction3;

    public JpegXlWeightedPredictor(JpegXlWeightedPredictorHeader header, int width, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(header);
        _header = header;
        _width = width;

        // Two rows (the current one and the previous one) of width + 2 entries, for the error of the prediction and of each sub-predictor
        var rowLength = (long)width + 2;
        _errors = Av1PooledArray<int>.Rent(scope, rowLength * 2);
        try
        {
            _subErrors = Av1PooledArray<uint>.Rent(scope, rowLength * 2 * 4);
        }
        catch
        {
            _errors.Dispose();
            throw;
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
        var rowLength = _width + 2;
        var currentRow = (y & 1) != 0 ? 0 : rowLength;
        var previousRow = (y & 1) != 0 ? rowLength : 0;
        var positionN = previousRow + x;
        var positionNE = x < _width - 1 ? positionN + 1 : positionN;
        var positionNW = x > 0 ? positionN - 1 : positionN;
        var errors = _errors.Span;
        var subErrors = _subErrors.Span;

        Span<uint> weights = stackalloc uint[4];
        for (var i = 0; i < 4; i++)
        {
            var offset = i * rowLength * 2;
            var sum = subErrors[offset + positionN] + subErrors[offset + positionNE] + subErrors[offset + positionNW];
            weights[i] = GetErrorWeight(sum, _header.Weights[i]);
        }

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

        long largest = errorW;
        if (Math.Abs(errorN) > Math.Abs(largest))
        {
            largest = errorN;
        }

        if (Math.Abs(errorNW) > Math.Abs(largest))
        {
            largest = errorNW;
        }

        if (Math.Abs(errorNE) > Math.Abs(largest))
        {
            largest = errorNE;
        }

        maxError = (int)largest;

        _prediction0 = west + northEast - north;
        _prediction1 = north - (((sumWN + errorNE) * _header.P1) >> 5);
        _prediction2 = west - (((sumWN + errorNW) * _header.P2) >> 5);
        _prediction3 = north - (((errorNW * _header.P3A) + (errorN * _header.P3B) + (errorNE * _header.P3C) + ((northNorth - north) * _header.P3D) + ((northWest - west) * _header.P3E)) >> 5);

        // The weighted average: the weights are reduced to 4 bits of precision above their leading bit
        var weightSum = weights[0] + weights[1] + weights[2] + weights[3];
        var shift = BitOperations.Log2(weightSum) - 4;
        weightSum = 0;
        for (var i = 0; i < 4; i++)
        {
            weights[i] >>= shift;
            weightSum += weights[i];
        }

        var total = (long)(weightSum >> 1) - 1 + (_prediction0 * weights[0]) + (_prediction1 * weights[1]) + (_prediction2 * weights[2]) + (_prediction3 * weights[3]);
        _prediction = (total * Reciprocals[(int)weightSum - 1]) >> 24;

        // When the errors of the neighbors do not all have the same sign, the prediction is kept; otherwise it is clamped to the neighbors
        if (((errorN ^ errorW) | (errorN ^ errorNW)) > 0)
            return (_prediction + Rounding) >> ExtraBits;

        var max = Math.Max(west, Math.Max(northEast, north));
        var min = Math.Min(west, Math.Min(northEast, north));
        _prediction = Math.Max(min, Math.Min(max, _prediction));
        return (_prediction + Rounding) >> ExtraBits;
    }

    /// <summary>Records the errors of the last prediction once the sample is known.</summary>
    public void Update(int value, int x, int y)
    {
        var rowLength = _width + 2;
        var currentRow = (y & 1) != 0 ? 0 : rowLength;
        var previousRow = (y & 1) != 0 ? rowLength : 0;
        var scaled = (long)value << ExtraBits;
        _errors.Span[currentRow + x] = (int)(_prediction - scaled);
        var subErrors = _subErrors.Span;
        Update(subErrors, 0, _prediction0);
        Update(subErrors, 1, _prediction1);
        Update(subErrors, 2, _prediction2);
        Update(subErrors, 3, _prediction3);

        void Update(Span<uint> subErrors, int index, long prediction)
        {
            var error = (uint)((Math.Abs(prediction - scaled) + Rounding) >> ExtraBits);
            var offset = index * rowLength * 2;
            subErrors[offset + currentRow + x] = error;

            // The error also counts for the sample on the right of the one above
            subErrors[offset + previousRow + x + 1] += error;
        }
    }

    public void Dispose()
    {
        _errors.Dispose();
        _subErrors.Dispose();
    }

    private static uint GetErrorWeight(uint error, uint maxWeight)
    {
        var shift = Math.Max(0, BitOperations.Log2((ulong)error + 1) - 5);
        return 4 + ((maxWeight * Reciprocals[(int)(error >> shift)]) >> shift);
    }
}
