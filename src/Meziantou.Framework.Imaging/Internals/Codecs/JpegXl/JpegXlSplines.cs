namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The splines of a frame (ISO/IEC 18181-1, sections C.4.6 and K.4): curves through control points, drawn onto the XYB
/// samples before the color transform with a color and a thickness that vary along the curve.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>
/// A curve is a centripetal Catmull-Rom spline through its control points. It is walked in steps of one sample; at each
/// step a Gaussian spot is added, whose color and standard deviation are the values, at that fraction of the length of
/// the curve, of functions stored as 32 DCT coefficients.
/// </description></item>
/// <item><description>
/// The curves are walked twice, once to measure them, and the spots are drawn as they are produced: nothing is stored per
/// step, since a few bytes can describe a very long curve. The total area of the spots is bounded as the reference
/// decoder bounds it.
/// </description></item>
/// <item><description>
/// The spots are computed with an error function that is accurate to 1.5e-7. libjxl and jxl-oxide use a faster
/// approximation, accurate to 7e-4: where a spline is drawn, their samples differ from these by up to half a percent of
/// the range.
/// </description></item>
/// </list>
/// </remarks>
internal sealed class JpegXlSplines : IDisposable
{
    private const int ContextCount = 6;
    private const int ContextQuantizationAdjustment = 0;
    private const int ContextStartingPosition = 1;
    private const int ContextSplineCount = 2;
    private const int ContextControlPointCount = 3;
    private const int ContextControlPoint = 4;
    private const int ContextDct = 5;

    private const int MaxControlPoints = 1 << 20;
    private const int PositionLimit = 1 << 23;
    private const int DeltaLimit = 1 << 30;
    private const int CoefficientCount = 32;

    /// <summary>The coefficients of a spline: three colors and the thickness.</summary>
    private const int CoefficientsPerSpline = 4 * CoefficientCount;

    /// <summary>The number of points computed between two control points.</summary>
    private const int PointsPerSegment = 16;

    private readonly AllocationScope _scope;
    private Av1PooledArray<int>? _startingPoints;
    private Av1PooledArray<int>? _controlPointOffsets;
    private Av1PooledArray<int>? _controlPoints;
    private Av1PooledArray<int>? _coefficients;
    private int _splineCount;
    private int _quantizationAdjustment;

    private JpegXlSplines(AllocationScope scope) => _scope = scope;

    private static ReadOnlySpan<float> ChannelWeights => [0.0042f, 0.075f, 0.07f, 0.3333f];

    /// <summary>Reads the splines of a frame, in <c>LfGlobal</c>.</summary>
    /// <param name="reader">The reader.</param>
    /// <param name="pixelCount">The number of samples of a channel of the frame.</param>
    /// <param name="scope">The scope of the splines.</param>
    public static JpegXlSplines Read(JpegXlBitReader reader, long pixelCount, AllocationScope scope)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(scope);
        JpegXlSplines? owned = null;
        try
        {
            var result = owned = new JpegXlSplines(scope);
            result.ReadCore(reader, pixelCount);
            owned = null;
            return result;
        }
        finally
        {
            owned?.Dispose();
        }
    }

    /// <summary>Draws the splines onto the XYB samples of a frame.</summary>
    /// <param name="frame">The samples of the frame: X, Y and B.</param>
    /// <param name="baseCorrelationX">The base factor of the prediction of X from Y of the frame.</param>
    /// <param name="baseCorrelationB">The base factor of the prediction of B from Y of the frame.</param>
    /// <param name="cancellationToken">The token checked between steps.</param>
    public void Draw(JpegXlImageBuffer frame, float baseCorrelationX, float baseCorrelationB, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.ColorChannelCount != 3)
            throw JpegXlFormat.Invalid("a frame without three color channels has splines.");

        var imageSize = (ulong)frame.Width * (ulong)frame.Height;
        var areaLimit = Math.Min((1024 * imageSize) + (1UL << 32), 1UL << 42);
        var inverseQuant = _quantizationAdjustment >= 0 ? 1f / (1f + (0.125f * _quantizationAdjustment)) : 1f - (0.125f * _quantizationAdjustment);
        var offsets = _controlPointOffsets!.Span;
        Span<float> color = stackalloc float[3 * CoefficientCount];
        Span<float> sigma = stackalloc float[CoefficientCount];
        ulong totalArea = 0;

        // Every spline is checked before any is drawn
        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < _splineCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var deltas = _controlPoints!.Span.Slice(offsets[i] * 2, (offsets[i + 1] - offsets[i]) * 2);
                var quantized = _coefficients!.Span.Slice(i * CoefficientsPerSpline, CoefficientsPerSpline);
                var startX = _startingPoints!.Span[i * 2];
                var startY = _startingPoints.Span[(i * 2) + 1];
                if (pass == 0)
                {
                    totalArea += Validate(startX, startY, deltas, quantized, inverseQuant, baseCorrelationX, baseCorrelationB, areaLimit);
                    if (totalArea > areaLimit)
                        throw JpegXlFormat.Invalid("the splines of a frame cover too large an area.");

                    continue;
                }

                // The functions of the color and of the thickness; X and B are predicted from Y
                for (var c = 0; c < 3; c++)
                {
                    for (var k = 0; k < CoefficientCount; k++)
                    {
                        color[(c * CoefficientCount) + k] = quantized[(c * CoefficientCount) + k] * (k == 0 ? 0.70710678118654752440f : 1f) * ChannelWeights[c] * inverseQuant;
                    }
                }

                for (var k = 0; k < CoefficientCount; k++)
                {
                    color[k] += baseCorrelationX * color[CoefficientCount + k];
                    color[(2 * CoefficientCount) + k] += baseCorrelationB * color[CoefficientCount + k];
                    sigma[k] = quantized[(3 * CoefficientCount) + k] * (k == 0 ? 0.70710678118654752440f : 1f) * ChannelWeights[3] * inverseQuant;
                }

                DrawSpline(frame, startX, startY, deltas, color, sigma, cancellationToken);
            }
        }
    }

    public void Dispose()
    {
        _startingPoints?.Dispose();
        _startingPoints = null;
        _controlPointOffsets?.Dispose();
        _controlPointOffsets = null;
        _controlPoints?.Dispose();
        _controlPoints = null;
        _coefficients?.Dispose();
        _coefficients = null;
    }

    private void ReadCore(JpegXlBitReader reader, long pixelCount)
    {
        using var code = JpegXlEntropyCode.Read(reader, ContextCount, _scope);
        using var decoder = new JpegXlEntropyDecoder(code, _scope);
        decoder.Begin(reader);

        // A value may take no bit: the counts are bounded by the size of the frame
        var maxControlPoints = Math.Min(MaxControlPoints, pixelCount / 2);
        var count = (long)decoder.Read(reader, ContextSplineCount) + 1;
        if (count > maxControlPoints)
            throw JpegXlFormat.Invalid("a frame has too many splines.");

        _splineCount = (int)count;
        _startingPoints = Av1PooledArray<int>.Rent(_scope, 2L * _splineCount, clear: false);
        _controlPointOffsets = Av1PooledArray<int>.Rent(_scope, _splineCount + 1L, clear: false);
        _coefficients = Av1PooledArray<int>.Rent(_scope, (long)_splineCount * CoefficientsPerSpline, clear: false);
        long lastX = 0;
        long lastY = 0;
        for (var i = 0; i < _splineCount; i++)
        {
            // The first position, then the difference with the previous one
            var x = (long)decoder.Read(reader, ContextStartingPosition);
            var y = (long)decoder.Read(reader, ContextStartingPosition);
            if (i != 0)
            {
                x = JpegXlBitReader.UnpackSigned((uint)x) + lastX;
                y = JpegXlBitReader.UnpackSigned((uint)y) + lastY;
            }

            EnsurePosition(x, y);
            _startingPoints.Span[i * 2] = (int)x;
            _startingPoints.Span[(i * 2) + 1] = (int)y;
            lastX = x;
            lastY = y;
        }

        _quantizationAdjustment = JpegXlBitReader.UnpackSigned(decoder.Read(reader, ContextQuantizationAdjustment));
        long totalControlPoints = _splineCount;
        var stored = 0;
        for (var i = 0; i < _splineCount; i++)
        {
            var controlPointCount = decoder.Read(reader, ContextControlPointCount);
            totalControlPoints += controlPointCount;
            if (controlPointCount > maxControlPoints || totalControlPoints > maxControlPoints)
                throw JpegXlFormat.Invalid("a frame has too many spline control points.");

            _controlPointOffsets.Span[i] = stored;
            EnsureControlPointCapacity(stored + (int)controlPointCount);
            var points = _controlPoints!.Span.Slice(stored * 2, (int)controlPointCount * 2);
            for (var j = 0; j < points.Length; j++)
            {
                // The differences of the differences of the positions
                var value = JpegXlBitReader.UnpackSigned(decoder.Read(reader, ContextControlPoint));
                if (value is >= DeltaLimit or <= -DeltaLimit)
                    throw JpegXlFormat.Invalid("a spline control point is out of range.");

                points[j] = value;
            }

            stored += (int)controlPointCount;
            var coefficients = _coefficients.Span.Slice(i * CoefficientsPerSpline, CoefficientsPerSpline);
            for (var j = 0; j < coefficients.Length; j++)
            {
                coefficients[j] = JpegXlBitReader.UnpackSigned(decoder.Read(reader, ContextDct));
                if (coefficients[j] == int.MinValue)
                    throw JpegXlFormat.Invalid("a spline coefficient is out of range.");
            }

            reader.EnsureNotOverrun();
        }

        _controlPointOffsets.Span[_splineCount] = stored;
        decoder.EnsureFinalState();
    }

    private void EnsureControlPointCapacity(int count)
    {
        if (_controlPoints is not null && _controlPoints.Length >= count * 2)
            return;

        var larger = Av1PooledArray<int>.Rent(_scope, Math.Max(64, 4L * count), clear: false);
        _controlPoints?.Span.CopyTo(larger.Span);
        _controlPoints?.Dispose();
        _controlPoints = larger;
    }

    private static void EnsurePosition(long x, long y)
    {
        if (x is >= PositionLimit or <= -PositionLimit || y is >= PositionLimit or <= -PositionLimit)
            throw JpegXlFormat.Invalid("a spline is outside the range of positions.");
    }

    /// <summary>Checks the positions of a spline and estimates the area it covers, as the reference decoder does to bound the decoding time.</summary>
    private static ulong Validate(int startX, int startY, ReadOnlySpan<int> deltas, ReadOnlySpan<int> quantized, float inverseQuant, float baseCorrelationX, float baseCorrelationB, ulong areaLimit)
    {
        long x = startX;
        long y = startY;
        long deltaX = 0;
        long deltaY = 0;
        ulong manhattanDistance = 0;
        for (var i = 0; i < deltas.Length; i += 2)
        {
            deltaX += deltas[i];
            deltaY += deltas[i + 1];
            manhattanDistance += (ulong)(Math.Abs(deltaX) + Math.Abs(deltaY));
            if (manhattanDistance > areaLimit)
                throw JpegXlFormat.Invalid("a spline is too long.");

            EnsurePosition(deltaX, deltaY);
            x += deltaX;
            y += deltaY;
            EnsurePosition(x, y);
            if (deltaX == 0 && deltaY == 0)
                throw JpegXlFormat.Invalid("a spline has two identical successive control points.");
        }

        Span<ulong> color = stackalloc ulong[3];
        for (var c = 0; c < 3; c++)
        {
            for (var k = 0; k < CoefficientCount; k++)
            {
                color[c] += (ulong)MathF.Ceiling(inverseQuant * MathF.Abs(quantized[(c * CoefficientCount) + k]));
            }
        }

        color[0] += (ulong)MathF.Ceiling(MathF.Abs(baseCorrelationX)) * color[1];
        color[2] += (ulong)MathF.Ceiling(MathF.Abs(baseCorrelationB)) * color[1];
        var maxColor = Math.Max(color[1], Math.Max(color[0], color[2]));
        var logColor = Math.Max(1UL, (ulong)(64 - System.Numerics.BitOperations.LeadingZeroCount(maxColor)));
        var weightLimit = MathF.Ceiling(MathF.Sqrt((float)areaLimit / logColor / Math.Max(1UL, manhattanDistance)));
        ulong widthEstimate = 0;
        for (var k = 0; k < CoefficientCount; k++)
        {
            var weight = (ulong)MathF.Min(weightLimit, MathF.Max(1f, MathF.Ceiling(inverseQuant * MathF.Abs(quantized[(3 * CoefficientCount) + k]))));
            widthEstimate += weight * weight * logColor;
        }

        return widthEstimate * manhattanDistance;
    }

    private static void DrawSpline(JpegXlImageBuffer frame, int startX, int startY, ReadOnlySpan<int> deltas, ReadOnlySpan<float> color, ReadOnlySpan<float> sigma, CancellationToken cancellationToken)
    {
        // The curve is measured first: the color and the thickness depend on the fraction of its length
        var walker = new Walker(startX, startY, deltas);
        long stepCount = 0;
        var lastStep = 0f;
        while (walker.MoveNext())
        {
            stepCount++;
            lastStep = walker.Multiplier;
            if ((stepCount & 0xFFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        var arcLength = (stepCount - 2) + lastStep;
        if (!(arcLength > 0))
            return;

        var inverseArcLength = 1f / arcLength;
        walker = new Walker(startX, startY, deltas);
        long step = 0;
        Span<float> spotColor = stackalloc float[3];
        while (walker.MoveNext())
        {
            var progress = MathF.Min(1f, step * inverseArcLength);
            step++;
            if ((step & 0xFFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var position = (CoefficientCount - 1) * progress;
            for (var c = 0; c < 3; c++)
            {
                spotColor[c] = Evaluate(color.Slice(c * CoefficientCount, CoefficientCount), position);
            }

            DrawSpot(frame, walker.X, walker.Y, walker.Multiplier, spotColor, Evaluate(sigma, position));
        }
    }

    /// <summary>The value at a position, which may be between two samples, of a function given by the DCT of 32 samples.</summary>
    private static float Evaluate(ReadOnlySpan<float> coefficients, float position)
    {
        var sum = 0f;
        var scaled = position + 0.5f;
        for (var i = 0; i < CoefficientCount; i++)
        {
            sum += 1.41421356237309504880f * (coefficients[i] * MathF.Cos(MathF.PI / 32 * i * scaled));
        }

        return sum;
    }

    /// <summary>Adds a Gaussian spot: the integral of the Gaussian over each sample, approximated in one dimension along the distance to the center.</summary>
    private static void DrawSpot(JpegXlImageBuffer frame, float centerX, float centerY, float intensity, ReadOnlySpan<float> color, float sigma)
    {
        if (!float.IsFinite(sigma) || sigma == 0 || !float.IsFinite(1f / sigma) || !float.IsFinite(intensity))
            return;

        // Beyond this distance the spot adds less than a hundred-thousandth
        var maxColor = 0.01f;
        for (var c = 0; c < 3; c++)
        {
            maxColor = MathF.Max(maxColor, MathF.Abs(color[c] * intensity));
        }

        var maxDistance = MathF.Sqrt(-2f * sigma * sigma * ((MathF.Log(0.1f) * 5) - MathF.Log(maxColor)));
        if (!float.IsFinite(maxDistance))
            return;

        var top = Math.Max(0L, (long)MathF.Round(centerY - maxDistance, MidpointRounding.AwayFromZero));
        var bottom = Math.Min(frame.Height, (long)MathF.Round(centerY + maxDistance, MidpointRounding.AwayFromZero) + 1);
        var left = Math.Max(0L, (long)MathF.Round(centerX - maxDistance, MidpointRounding.AwayFromZero));
        var right = Math.Min(frame.Width, (long)MathF.Round(centerX + maxDistance, MidpointRounding.AwayFromZero) + 1);
        if (bottom <= top || right <= left)
            return;

        var inverseSigma = 1f / sigma;
        var scale = 0.25f * sigma * intensity;
        for (var y = (int)top; y < (int)bottom; y++)
        {
            var rowX = frame.GetRow(0, y);
            var rowY = frame.GetRow(1, y);
            var rowB = frame.GetRow(2, y);
            var dy = y - centerY;
            for (var x = (int)left; x < (int)right; x++)
            {
                var dx = x - centerX;
                var distance = MathF.Sqrt((dx * dx) + (dy * dy));
                var factor = Erf(((distance * 0.5f) + 0.353553391f) * inverseSigma) - Erf(((distance * 0.5f) - 0.353553391f) * inverseSigma);
                var local = scale * (factor * factor);
                rowX[x] += color[0] * local;
                rowY[x] += color[1] * local;
                rowB[x] += color[2] * local;
            }
        }
    }

    /// <summary>The error function, within 1.5e-7 (Abramowitz and Stegun, formula 7.1.26).</summary>
    private static float Erf(float value)
    {
        var x = Math.Abs((double)value);
        var t = 1.0 / (1.0 + (0.3275911 * x));
        var polynomial = t * (0.254829592 + (t * (-0.284496736 + (t * (1.421413741 + (t * (-1.453152027 + (t * 1.061405429))))))));
        var result = 1.0 - (polynomial * Math.Exp(-x * x));
        return (float)(value < 0 ? -result : result);
    }

    /// <summary>
    /// Walks a spline in steps of one sample: the points of the centripetal Catmull-Rom curve through the control points
    /// (16 per interval) are joined by straight lines, along which a point is produced at every unit of length. The last
    /// point is the end of the curve, with the length that remained.
    /// </summary>
    private ref struct Walker
    {
        private readonly ReadOnlySpan<int> _deltas;
        private readonly int _controlPointCount;

        // The control points around the interval that is being computed: one before, its two ends, one after
        private float _p0X;
        private float _p0Y;
        private float _p1X;
        private float _p1Y;
        private float _p2X;
        private float _p2Y;
        private float _p3X;
        private float _p3Y;
        private long _nextX;
        private long _nextY;
        private long _deltaX;
        private long _deltaY;
        private int _interval;
        private int _subdivision;
        private bool _hasCurvePoint;
        private bool _curveEnded;
        private float _curveX;
        private float _curveY;
        private float _previousX;
        private float _previousY;
        private float _lengthFromPrevious;
        private int _state;
        private int _readCount;

        public Walker(int startX, int startY, ReadOnlySpan<int> deltas)
        {
            _deltas = deltas;
            _controlPointCount = (deltas.Length / 2) + 1;
            _p1X = startX;
            _p1Y = startY;
            _nextX = startX;
            _nextY = startY;
            if (_controlPointCount > 1)
            {
                ReadControlPoint(out _p2X, out _p2Y);

                // The point before the first one mirrors the second one
                _p0X = _p1X + (_p1X - _p2X);
                _p0Y = _p1Y + (_p1Y - _p2Y);
                LoadFollowingPoint();
            }
        }

        public float X { get; private set; }

        public float Y { get; private set; }

        /// <summary>Gets the length that the point stands for: one, or what remained at the end of the curve.</summary>
        public float Multiplier { get; private set; }

        public bool MoveNext()
        {
            switch (_state)
            {
                case 0:
                    // The first point of the curve
                    AdvanceCurve();
                    X = _curveX;
                    Y = _curveY;
                    Multiplier = 1;
                    _previousX = X;
                    _previousY = Y;
                    _lengthFromPrevious = 0;
                    _state = 1;
                    return true;

                case 1:
                    while (true)
                    {
                        if (!_hasCurvePoint)
                        {
                            // The end of the curve: the length that remained since the last point
                            X = _previousX;
                            Y = _previousY;
                            Multiplier = _lengthFromPrevious;
                            _state = 2;
                            return true;
                        }

                        var dx = _curveX - _previousX;
                        var dy = _curveY - _previousY;
                        var lengthToNext = MathF.Sqrt((dx * dx) + (dy * dy));
                        if (_lengthFromPrevious + lengthToNext >= 1f)
                        {
                            var fraction = (1f - _lengthFromPrevious) / lengthToNext;
                            X = _previousX + (fraction * dx);
                            Y = _previousY + (fraction * dy);
                            Multiplier = 1;
                            _previousX = X;
                            _previousY = Y;
                            _lengthFromPrevious = 0;
                            return true;
                        }

                        _lengthFromPrevious += lengthToNext;
                        _previousX = _curveX;
                        _previousY = _curveY;
                        AdvanceCurve();
                    }

                default:
                    return false;
            }
        }

        /// <summary>Computes the next point of the curve.</summary>
        private void AdvanceCurve()
        {
            if (_curveEnded)
            {
                _hasCurvePoint = false;
                return;
            }

            _hasCurvePoint = true;
            if (_controlPointCount == 1 || _interval == _controlPointCount - 1)
            {
                // The last control point ends the curve
                _curveX = _p1X;
                _curveY = _p1Y;
                _curveEnded = true;
                return;
            }

            if (_subdivision == 0)
            {
                _curveX = _p1X;
                _curveY = _p1Y;
            }
            else
            {
                // The parameter of a control point is the sum of the square roots of the distances before it
                var d0 = MathF.Sqrt(MathF.Sqrt(((_p1X - _p0X) * (_p1X - _p0X)) + ((_p1Y - _p0Y) * (_p1Y - _p0Y))));
                var d1 = MathF.Sqrt(MathF.Sqrt(((_p2X - _p1X) * (_p2X - _p1X)) + ((_p2Y - _p1Y) * (_p2Y - _p1Y))));
                var d2 = MathF.Sqrt(MathF.Sqrt(((_p3X - _p2X) * (_p3X - _p2X)) + ((_p3Y - _p2Y) * (_p3Y - _p2Y))));
                var t1 = d0;
                var t2 = t1 + d1;
                var t = d0 + (((float)_subdivision / PointsPerSegment) * d1);
                var a0X = _p0X + ((t / d0) * (_p1X - _p0X));
                var a0Y = _p0Y + ((t / d0) * (_p1Y - _p0Y));
                var a1X = _p1X + (((t - t1) / d1) * (_p2X - _p1X));
                var a1Y = _p1Y + (((t - t1) / d1) * (_p2Y - _p1Y));
                var a2X = _p2X + (((t - t2) / d2) * (_p3X - _p2X));
                var a2Y = _p2Y + (((t - t2) / d2) * (_p3Y - _p2Y));
                var b0X = a0X + ((t / (d0 + d1)) * (a1X - a0X));
                var b0Y = a0Y + ((t / (d0 + d1)) * (a1Y - a0Y));
                var b1X = a1X + (((t - t1) / (d1 + d2)) * (a2X - a1X));
                var b1Y = a1Y + (((t - t1) / (d1 + d2)) * (a2Y - a1Y));
                _curveX = b0X + (((t - t1) / d1) * (b1X - b0X));
                _curveY = b0Y + (((t - t1) / d1) * (b1Y - b0Y));
            }

            if (++_subdivision == PointsPerSegment)
            {
                // The next interval
                _subdivision = 0;
                _interval++;
                _p0X = _p1X;
                _p0Y = _p1Y;
                _p1X = _p2X;
                _p1Y = _p2Y;
                _p2X = _p3X;
                _p2Y = _p3Y;
                LoadFollowingPoint();
            }
        }

        /// <summary>Sets the control point after the interval: the next one, or the mirror image of the one before the last.</summary>
        private void LoadFollowingPoint()
        {
            if (_interval + 2 < _controlPointCount)
            {
                ReadControlPoint(out _p3X, out _p3Y);
            }
            else
            {
                _p3X = _p2X + (_p2X - _p1X);
                _p3Y = _p2Y + (_p2Y - _p1Y);
            }
        }

        private void ReadControlPoint(out float x, out float y)
        {
            // The control points are stored as the differences of their differences
            var index = _readCount++ * 2;
            _deltaX += _deltas[index];
            _deltaY += _deltas[index + 1];
            _nextX += _deltaX;
            _nextY += _deltaY;
            x = _nextX;
            y = _nextY;
        }
    }
}
