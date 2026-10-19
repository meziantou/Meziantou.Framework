using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The film grain synthesis process of AV1 (AV1 specification, section 7.18.3): adds pseudo-random noise, shaped by the
/// parameters of the frame header, to the samples of an output frame. The reference frames are never modified: the grain is
/// applied to the rows that are output, by stripes of 32 luma rows, which must be requested in increasing order.
/// </summary>
/// <remarks>
/// With 128-bit hardware vectors, the auto-regressive filter of the grain templates sums the taps of the rows above a row
/// with vectors, and the rows of a stripe are processed by chunks: the scaling of every sample is looked up first (above 8
/// bits, in a table of the interpolated scaling of every sample value, built from the same function), then the scaled noise
/// is added to 8 samples at a time, with the arithmetic of the scalar references.
/// </remarks>
internal sealed class Av1FilmGrain : IDisposable
{
    /// <summary>The number of luma rows of a stripe.</summary>
    public const int StripeHeight = 32;

    private const int LumaGrainWidth = 82;
    private const int LumaGrainHeight = 73;
    private const int NoiseStripeRows = 34;

    // The number of samples of a row processed at a time by the vector path: the size of its scratch on the stack
    private const int ChunkLength = 256;

    private readonly Av1FilmGrainParameters _parameters;
    private readonly int _bitDepth;
    private readonly int _planeCount;
    private readonly int _subX;
    private readonly int _subY;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _identityMatrix;
    private readonly int _grainMin;
    private readonly int _grainMax;
    private readonly int _chromaGrainWidth;
    private readonly int _chromaGrainHeight;
    private readonly int _noiseStride;
    private readonly int _expandedLength;
    private readonly bool _vectorized;
    private Av1PooledArray<short>? _buffer;
    private int _randomRegister;
    private int _currentStripe = -1;

    /// <summary>Prepares the grain of a frame of <paramref name="width"/> x <paramref name="height"/> luma samples.</summary>
    /// <exception cref="ImageResourceLimitException">The working buffers exceed the live-allocation limit.</exception>
    public Av1FilmGrain(AllocationScope scope, Av1SequenceHeader sequence, Av1FilmGrainParameters parameters, int width, int height)
        : this(scope, sequence, parameters, width, height, Vector128.IsHardwareAccelerated)
    {
    }

    /// <summary>
    /// Prepares the grain of a frame, with the vector paths when <paramref name="vectorized"/> is <see langword="true"/> and
    /// the hardware supports them, or with the scalar references (the tests compare both).
    /// </summary>
    internal Av1FilmGrain(AllocationScope scope, Av1SequenceHeader sequence, Av1FilmGrainParameters parameters, int width, int height, bool vectorized)
    {
        _vectorized = vectorized && Vector128.IsHardwareAccelerated;
        _parameters = parameters;
        _bitDepth = sequence.BitDepth;
        _planeCount = sequence.PlaneCount;
        _subX = sequence.SubsamplingX;
        _subY = sequence.SubsamplingY;
        _width = width;
        _height = height;
        _identityMatrix = sequence.MatrixCoefficients is Av1Constants.McIdentity;
        var grainCenter = 128 << (_bitDepth - 8);
        _grainMin = -grainCenter;
        _grainMax = (256 << (_bitDepth - 8)) - 1 - grainCenter;
        _chromaGrainWidth = _subX is 1 ? 44 : LumaGrainWidth;
        _chromaGrainHeight = _subY is 1 ? 38 : LumaGrainHeight;

        // A noise stripe is written a few samples beyond the width of the frame
        _noiseStride = width + NoiseStripeRows + 2;
        _expandedLength = _bitDepth > 8 ? 1 << _bitDepth : 0;
        _buffer = Av1PooledArray<short>.Rent(scope, GrainLength + ScalingLength + (3L * _expandedLength) + (2L * 3 * NoiseStripeRows * _noiseStride));
        GenerateGrain();
        InitializeScalingLookup();
    }

    private static int GrainLength => 3 * LumaGrainWidth * LumaGrainHeight;

    private static int ScalingLength => 3 * 256;

    private Span<short> Buffer => (_buffer ?? throw new ObjectDisposedException(nameof(Av1FilmGrain))).Span;

    // LumaGrain, CbGrain and CrGrain: each is stored with 82 samples per row
    private Span<short> GetGrain(int plane) => Buffer.Slice(plane * LumaGrainWidth * LumaGrainHeight, LumaGrainWidth * LumaGrainHeight);

    private Span<short> GetScalingLut(int plane) => Buffer.Slice(GrainLength + (plane * 256), 256);

    // ScaleLut of every sample value of a plane: the scaling lookup itself for 8-bit samples
    private Span<short> GetSampleScaling(int plane) => _expandedLength is 0 ? GetScalingLut(plane) : Buffer.Slice(GrainLength + ScalingLength + (plane * _expandedLength), _expandedLength);

    // noiseStripe[ lumaNum ][ plane ] for the stripe being output (which 0) and for the previous one (which 1)
    private Span<short> GetNoiseStripe(int which, int plane)
    {
        var slot = (_currentStripe + which) & 1;
        return Buffer.Slice(GrainLength + ScalingLength + (3 * _expandedLength) + (((slot * 3) + plane) * NoiseStripeRows * _noiseStride), NoiseStripeRows * _noiseStride);
    }

    /// <summary>Gets the number of stripes of the frame.</summary>
    public int StripeCount => (_height + StripeHeight - 1) / StripeHeight;

    // Section 7.18.3.2 "Random number process"
    private int GetRandomNumber(int bits)
    {
        var r = _randomRegister;
        var bit = (r ^ (r >> 1) ^ (r >> 3) ^ (r >> 12)) & 1;
        r = (r >> 1) | (bit << 15);
        _randomRegister = r;
        return (r >> (16 - bits)) & ((1 << bits) - 1);
    }

    private static int Round2(int value, int bits) => bits is 0 ? value : (value + (1 << (bits - 1))) >> bits;

    // Section 7.18.3.3 "Generate grain process"
    private void GenerateGrain()
    {
        var parameters = _parameters;
        var gaussian = Av1Tables.GaussianSequence;
        var luma = GetGrain(0);
        var shift = 12 - _bitDepth + parameters.GrainScaleShift;
        _randomRegister = parameters.GrainSeed;
        for (var y = 0; y < LumaGrainHeight; y++)
        {
            for (var x = 0; x < LumaGrainWidth; x++)
            {
                var g = parameters.NumYPoints > 0 ? gaussian[GetRandomNumber(11)] : 0;
                luma[(y * LumaGrainWidth) + x] = (short)Round2(g, shift);
            }
        }

        var lag = parameters.ArCoeffLag;
        var arShift = parameters.ArCoeffShift;
        if (parameters.NumYPoints > 0 && _vectorized)
        {
            ApplyAutoRegressionVector(luma, LumaGrainWidth, LumaGrainHeight, parameters.ArCoeffsY, lag, arShift, lumaGrain: default);
        }
        else if (parameters.NumYPoints > 0)
        {
            var coefficients = parameters.ArCoeffsY;
            for (var y = 3; y < LumaGrainHeight; y++)
            {
                for (var x = 3; x < LumaGrainWidth - 3; x++)
                {
                    var s = 0;
                    var pos = 0;
                    for (var deltaRow = -lag; deltaRow <= 0; deltaRow++)
                    {
                        for (var deltaCol = -lag; deltaCol <= lag; deltaCol++)
                        {
                            if (deltaRow is 0 && deltaCol is 0)
                                break;

                            s += luma[((y + deltaRow) * LumaGrainWidth) + x + deltaCol] * coefficients[pos];
                            pos++;
                        }
                    }

                    luma[(y * LumaGrainWidth) + x] = (short)Math.Clamp(luma[(y * LumaGrainWidth) + x] + Round2(s, arShift), _grainMin, _grainMax);
                }
            }
        }

        if (_planeCount is 1)
            return;

        var cb = GetGrain(1);
        var cr = GetGrain(2);
        var useCb = parameters.NumCbPoints > 0 || parameters.ChromaScalingFromLuma;
        var useCr = parameters.NumCrPoints > 0 || parameters.ChromaScalingFromLuma;
        _randomRegister = parameters.GrainSeed ^ 0xb524;
        for (var y = 0; y < _chromaGrainHeight; y++)
        {
            for (var x = 0; x < _chromaGrainWidth; x++)
            {
                var g = useCb ? gaussian[GetRandomNumber(11)] : 0;
                cb[(y * LumaGrainWidth) + x] = (short)Round2(g, shift);
            }
        }

        _randomRegister = parameters.GrainSeed ^ 0x49d8;
        for (var y = 0; y < _chromaGrainHeight; y++)
        {
            for (var x = 0; x < _chromaGrainWidth; x++)
            {
                var g = useCr ? gaussian[GetRandomNumber(11)] : 0;
                cr[(y * LumaGrainWidth) + x] = (short)Round2(g, shift);
            }
        }

        // A plane without grain has no coefficients: its grain is never read
        ReadOnlySpan<sbyte> coefficientsCb = useCb ? parameters.ArCoeffsCb : default;
        ReadOnlySpan<sbyte> coefficientsCr = useCr ? parameters.ArCoeffsCr : default;
        if (_vectorized)
        {
            // Without luma points, the coefficient of the luma grain is not coded
            ReadOnlySpan<short> lumaGrain = parameters.NumYPoints > 0 ? luma : default;
            ApplyAutoRegressionVector(cb, _chromaGrainWidth, _chromaGrainHeight, coefficientsCb, lag, arShift, lumaGrain);
            ApplyAutoRegressionVector(cr, _chromaGrainWidth, _chromaGrainHeight, coefficientsCr, lag, arShift, lumaGrain);
            return;
        }

        for (var y = 3; y < _chromaGrainHeight; y++)
        {
            for (var x = 3; x < _chromaGrainWidth - 3; x++)
            {
                var s0 = 0;
                var s1 = 0;
                var pos = 0;
                for (var deltaRow = -lag; deltaRow <= 0; deltaRow++)
                {
                    for (var deltaCol = -lag; deltaCol <= lag; deltaCol++)
                    {
                        // Without luma points, the coefficient of the luma grain is not coded
                        var c0 = pos < coefficientsCb.Length ? coefficientsCb[pos] : 0;
                        var c1 = pos < coefficientsCr.Length ? coefficientsCr[pos] : 0;
                        if (deltaRow is 0 && deltaCol is 0)
                        {
                            if (parameters.NumYPoints > 0)
                            {
                                var lumaSum = 0;
                                var lumaX = ((x - 3) << _subX) + 3;
                                var lumaY = ((y - 3) << _subY) + 3;
                                for (var i = 0; i <= _subY; i++)
                                {
                                    for (var j = 0; j <= _subX; j++)
                                    {
                                        lumaSum += luma[((lumaY + i) * LumaGrainWidth) + lumaX + j];
                                    }
                                }

                                lumaSum = Round2(lumaSum, _subX + _subY);
                                s0 += lumaSum * c0;
                                s1 += lumaSum * c1;
                            }

                            break;
                        }

                        s0 += cb[((y + deltaRow) * LumaGrainWidth) + x + deltaCol] * c0;
                        s1 += cr[((y + deltaRow) * LumaGrainWidth) + x + deltaCol] * c1;
                        pos++;
                    }
                }

                cb[(y * LumaGrainWidth) + x] = (short)Math.Clamp(cb[(y * LumaGrainWidth) + x] + Round2(s0, arShift), _grainMin, _grainMax);
                cr[(y * LumaGrainWidth) + x] = (short)Math.Clamp(cr[(y * LumaGrainWidth) + x] + Round2(s1, arShift), _grainMin, _grainMax);
            }
        }
    }

    // The auto-regressive filter of section 7.18.3.3 on a grain template of width x height samples (rows of 82 samples), for
    // the rows from 3 and the columns 3 to width - 4: the taps of the rows above, which are final, are added for the whole row
    // 4 columns at a time; the luma term of a chroma template (lumaGrain is not empty) and the taps on the left in the row,
    // which the filter has just written, are then added one sample at a time. The integer sums are those of the scalar code;
    // a missing coefficient (a template without grain has none) is 0.
    private void ApplyAutoRegressionVector(Span<short> grain, int width, int height, ReadOnlySpan<sbyte> coefficients, int lag, int shift, ReadOnlySpan<short> lumaGrain)
    {
        var count = width - 6;
        Span<int> sums = stackalloc int[LumaGrainWidth];
        sums = sums[..count];
        var rowTaps = (2 * lag) + 1;
        // The taps of the rows above come first, then the taps on the left, then the luma term
        var lumaPosition = (lag * rowTaps) + lag;
        var leftTaps = coefficients.Length >= lumaPosition ? coefficients.Slice(lag * rowTaps, lag) : default;
        var lumaCoefficient = !lumaGrain.IsEmpty && lumaPosition < coefficients.Length ? coefficients[lumaPosition] : 0;
        for (var y = 3; y < height; y++)
        {
            sums.Clear();
            for (var deltaRow = -lag; deltaRow < 0; deltaRow++)
            {
                var above = grain.Slice(((y + deltaRow) * LumaGrainWidth) + 3 - lag, count + (2 * lag));
                for (var deltaCol = -lag; deltaCol <= lag; deltaCol++)
                {
                    var position = ((deltaRow + lag) * rowTaps) + deltaCol + lag;
                    if (position < coefficients.Length && coefficients[position] is not 0)
                        AddTap(above.Slice(deltaCol + lag, count), coefficients[position], sums);
                }
            }

            if (lumaCoefficient is not 0)
            {
                var lumaY = ((y - 3) << _subY) + 3;
                var lumaRow = lumaGrain.Slice(lumaY * LumaGrainWidth, LumaGrainWidth);
                var nextLumaRow = lumaGrain.Slice((lumaY + _subY) * LumaGrainWidth, LumaGrainWidth);
                for (var x = 3; x < width - 3; x++)
                {
                    var lumaX = ((x - 3) << _subX) + 3;
                    int lumaSum = lumaRow[lumaX];
                    if (_subX is not 0)
                        lumaSum += lumaRow[lumaX + 1];

                    if (_subY is not 0)
                        lumaSum += _subX is 0 ? nextLumaRow[lumaX] : nextLumaRow[lumaX] + nextLumaRow[lumaX + 1];

                    sums[x - 3] += Round2(lumaSum, _subX + _subY) * lumaCoefficient;
                }
            }

            var row = grain.Slice(y * LumaGrainWidth, width);
            for (var x = 3; x < width - 3; x++)
            {
                var sum = sums[x - 3];
                for (var t = 0; t < leftTaps.Length; t++)
                {
                    sum += row[x - lag + t] * leftTaps[t];
                }

                row[x] = (short)Math.Clamp(row[x] + Round2(sum, shift), _grainMin, _grainMax);
            }
        }
    }

    // sums[i] += source[i] * coefficient, 4 values at a time
    private static void AddTap(ReadOnlySpan<short> source, int coefficient, Span<int> sums)
    {
        var multiplier = Vector128.Create(coefficient);
        var i = 0;
        for (; i <= sums.Length - 4; i += 4)
        {
            var samples = Vector128.WidenLower(Vector64.Create(source.Slice(i, 4)).ToVector128());
            (Vector128.Create(sums.Slice(i, 4)) + (samples * multiplier)).CopyTo(sums[i..]);
        }

        for (; i < sums.Length; i++)
        {
            sums[i] += source[i] * coefficient;
        }
    }

    // Section 7.18.3.4 "Scaling lookup initialization process"
    private void InitializeScalingLookup()
    {
        var parameters = _parameters;
        for (var plane = 0; plane < _planeCount; plane++)
        {
            ReadOnlySpan<byte> xs;
            ReadOnlySpan<byte> ys;
            if (plane is 0 || parameters.ChromaScalingFromLuma)
            {
                xs = parameters.PointYValue;
                ys = parameters.PointYScaling;
            }
            else if (plane is 1)
            {
                xs = parameters.PointCbValue;
                ys = parameters.PointCbScaling;
            }
            else
            {
                xs = parameters.PointCrValue;
                ys = parameters.PointCrScaling;
            }

            var lut = GetScalingLut(plane);
            var numPoints = xs.Length;
            if (numPoints is 0)
            {
                lut.Clear();
            }
            else
            {
                lut[..xs[0]].Fill(ys[0]);
                for (var i = 0; i < numPoints - 1; i++)
                {
                    var deltaY = ys[i + 1] - ys[i];
                    var deltaX = xs[i + 1] - xs[i];
                    var delta = deltaY * ((65536 + (deltaX >> 1)) / deltaX);
                    for (var x = 0; x < deltaX; x++)
                    {
                        lut[xs[i] + x] = (short)(ys[i] + (((x * delta) + 32768) >> 16));
                    }
                }

                lut[xs[numPoints - 1]..].Fill(ys[numPoints - 1]);
            }

            if (_expandedLength is not 0)
            {
                var scaling = GetSampleScaling(plane);
                for (var index = 0; index < scaling.Length; index++)
                {
                    scaling[index] = (short)ScaleLut(lut, index);
                }
            }
        }
    }

    private int ScaleLut(ReadOnlySpan<short> lut, int index)
    {
        var shift = _bitDepth - 8;
        var x = index >> shift;
        var rem = index - (x << shift);
        if (_bitDepth is 8 || x is 255)
            return lut[x];

        int start = lut[x];
        int end = lut[x + 1];
        return start + Round2((end - start) * rem, shift);
    }

    // The noiseStripe of section 7.18.3.5 for one stripe
    private void GenerateNoiseStripe(int lumaNum)
    {
        var parameters = _parameters;
        _randomRegister = parameters.GrainSeed;
        _randomRegister ^= (((lumaNum * 37) + 178) & 255) << 8;
        _randomRegister ^= ((lumaNum * 173) + 105) & 255;
        var overlap = parameters.OverlapFlag;
        for (var x = 0; x < (_width + 1) / 2; x += 16)
        {
            var rand = GetRandomNumber(8);
            var offsetX = rand >> 4;
            var offsetY = rand & 15;
            for (var plane = 0; plane < _planeCount; plane++)
            {
                var planeSubX = plane > 0 ? _subX : 0;
                var planeSubY = plane > 0 ? _subY : 0;
                var planeOffsetX = planeSubX is 1 ? 6 + offsetX : 9 + (offsetX * 2);
                var planeOffsetY = planeSubY is 1 ? 6 + offsetY : 9 + (offsetY * 2);
                var grain = GetGrain(plane);
                var stripe = GetNoiseStripe(0, plane);
                var columns = NoiseStripeRows >> planeSubX;

                // With overlap, the first 2 columns (1 when subsampled) of a block are blended with the block on their left
                var blended = overlap && x > 0 ? 2 >> planeSubX : 0;
                for (var i = 0; i < (NoiseStripeRows >> planeSubY); i++)
                {
                    var grainRow = grain.Slice(((planeOffsetY + i) * LumaGrainWidth) + planeOffsetX, columns);
                    var stripeRow = stripe.Slice((i * _noiseStride) + (planeSubX is 0 ? x * 2 : x), columns);
                    for (var j = 0; j < blended; j++)
                    {
                        int g = grainRow[j];
                        int old = stripeRow[j];
                        if (planeSubX is 0)
                        {
                            g = j is 0 ? (old * 27) + (g * 17) : (old * 17) + (g * 27);
                        }
                        else
                        {
                            g = (old * 23) + (g * 22);
                        }

                        stripeRow[j] = (short)Math.Clamp(Round2(g, 5), _grainMin, _grainMax);
                    }

                    grainRow[blended..].CopyTo(stripeRow[blended..]);
                }
            }
        }
    }

    // noiseImage[ plane ][ y ][ x ] for a row of the stripe being output
    private int GetNoise(ReadOnlySpan<short> stripe, ReadOnlySpan<short> previous, int planeSubY, int i, int x)
    {
        int g = stripe[(i * _noiseStride) + x];
        if (!_parameters.OverlapFlag || _currentStripe is 0)
            return g;

        if (planeSubY is 0)
        {
            if (i < 2)
            {
                int old = previous[((i + 32) * _noiseStride) + x];
                g = i is 0 ? (old * 27) + (g * 17) : (old * 17) + (g * 27);
                g = Math.Clamp(Round2(g, 5), _grainMin, _grainMax);
            }
        }
        else if (i < 1)
        {
            int old = previous[((i + 16) * _noiseStride) + x];
            g = (old * 23) + (g * 22);
            g = Math.Clamp(Round2(g, 5), _grainMin, _grainMax);
        }

        return g;
    }

    /// <summary>
    /// Writes the rows of the stripe <paramref name="lumaNum"/> of <paramref name="frame"/> with their grain: 32 luma rows, and
    /// the chroma rows that correspond to them. The stripes are requested in increasing order, from 0.
    /// </summary>
    /// <param name="frame">The frame to output, without grain.</param>
    /// <param name="lumaNum">The index of the stripe.</param>
    /// <param name="y">The luma rows of the stripe: <paramref name="lumaStride"/> samples per row.</param>
    /// <param name="lumaStride">The number of samples of a row of <paramref name="y"/>.</param>
    /// <param name="u">The U rows of the stripe; ignored for a monochrome frame.</param>
    /// <param name="v">The V rows of the stripe; ignored for a monochrome frame.</param>
    /// <param name="chromaStride">The number of samples of a row of <paramref name="u"/> and <paramref name="v"/>.</param>
    public void ApplyStripe(Av1FrameBuffer frame, int lumaNum, Span<ushort> y, int lumaStride, Span<ushort> u, Span<ushort> v, int chromaStride)
    {
        var vectorized = _vectorized;
        if (lumaNum != _currentStripe + 1)
            throw new InvalidOperationException("The stripes of a frame are output in order.");

        _currentStripe = lumaNum;
        GenerateNoiseStripe(lumaNum);

        var parameters = _parameters;
        int minValue;
        int maxLuma;
        int maxChroma;
        if (parameters.ClipToRestrictedRange)
        {
            minValue = 16 << (_bitDepth - 8);
            maxLuma = 235 << (_bitDepth - 8);
            maxChroma = _identityMatrix ? maxLuma : 240 << (_bitDepth - 8);
        }
        else
        {
            minValue = 0;
            maxLuma = (256 << (_bitDepth - 8)) - 1;
            maxChroma = maxLuma;
        }

        var maxSample = (1 << _bitDepth) - 1;
        var scalingShift = parameters.GrainScaling;
        var lumaPlane = frame.GetPlane(0);
        if (_planeCount > 1)
        {
            var chromaWidth = (_width + _subX) >> _subX;
            var chromaHeight = (_height + _subY) >> _subY;
            var firstRow = lumaNum << (5 - _subY);
            var rowCount = Math.Min(StripeHeight >> _subY, chromaHeight - firstRow);
            for (var plane = 1; plane < 3; plane++)
            {
                var source = frame.GetPlane(plane);
                var destination = plane is 1 ? u : v;
                var hasGrain = (plane is 1 ? parameters.NumCbPoints : parameters.NumCrPoints) > 0 || parameters.ChromaScalingFromLuma;
                var lut = GetScalingLut(plane);
                var scaling = GetSampleScaling(plane);
                var stripe = GetNoiseStripe(0, plane);
                var previous = GetNoiseStripe(1, plane);
                var lumaMult = (plane is 1 ? parameters.CbLumaMult : parameters.CrLumaMult) - 128;
                var mult = (plane is 1 ? parameters.CbMult : parameters.CrMult) - 128;
                var offset = ((plane is 1 ? parameters.CbOffset : parameters.CrOffset) - 256) << (_bitDepth - 8);
                for (var i = 0; i < rowCount; i++)
                {
                    var sourceRow = source.GetRow(firstRow + i);
                    var destinationRow = destination.Slice(i * chromaStride, chromaWidth);
                    if (!hasGrain)
                    {
                        sourceRow[..chromaWidth].CopyTo(destinationRow);
                        continue;
                    }

                    var lumaRow = lumaPlane.GetRow((firstRow + i) << _subY);
                    if (vectorized)
                    {
                        ApplyChromaRow(sourceRow[..chromaWidth], lumaRow, destinationRow, scaling, stripe, previous, i, lumaMult, mult, offset, maxSample, minValue, maxChroma);
                        continue;
                    }

                    for (var x = 0; x < chromaWidth; x++)
                    {
                        var lumaX = x << _subX;
                        var averageLuma = _subX is 1 ? (lumaRow[lumaX] + lumaRow[Math.Min(lumaX + 1, _width - 1)] + 1) >> 1 : lumaRow[lumaX];
                        int orig = sourceRow[x];
                        int merged;
                        if (parameters.ChromaScalingFromLuma)
                        {
                            merged = averageLuma;
                        }
                        else
                        {
                            var combined = (averageLuma * lumaMult) + (orig * mult);
                            merged = Math.Clamp((combined >> 6) + offset, 0, maxSample);
                        }

                        var noise = GetNoise(stripe, previous, _subY, i, x);
                        noise = Round2(ScaleLut(lut, merged) * noise, scalingShift);
                        destinationRow[x] = (ushort)Math.Clamp(orig + noise, minValue, maxChroma);
                    }
                }
            }
        }

        var lumaLut = GetScalingLut(0);
        var lumaScaling = GetSampleScaling(0);
        var lumaStripe = GetNoiseStripe(0, 0);
        var previousLumaStripe = GetNoiseStripe(1, 0);
        var firstLumaRow = lumaNum * StripeHeight;
        var lumaRowCount = Math.Min(StripeHeight, _height - firstLumaRow);
        for (var i = 0; i < lumaRowCount; i++)
        {
            var sourceRow = lumaPlane.GetRow(firstLumaRow + i);
            var destinationRow = y.Slice(i * lumaStride, _width);
            if (parameters.NumYPoints is 0)
            {
                sourceRow[.._width].CopyTo(destinationRow);
                continue;
            }

            if (vectorized)
            {
                ApplyLumaRow(sourceRow[.._width], destinationRow, lumaScaling, lumaStripe, previousLumaStripe, i, minValue, maxLuma);
                continue;
            }

            for (var x = 0; x < _width; x++)
            {
                int orig = sourceRow[x];
                var noise = GetNoise(lumaStripe, previousLumaStripe, 0, i, x);
                noise = Round2(ScaleLut(lumaLut, orig) * noise, scalingShift);
                destinationRow[x] = (ushort)Math.Clamp(orig + noise, minValue, maxLuma);
            }
        }
    }

    // The vector path of a luma row, by chunks: the scaling of every sample, then the scaled noise
    private void ApplyLumaRow(ReadOnlySpan<ushort> sourceRow, Span<ushort> destinationRow, ReadOnlySpan<short> scaling, ReadOnlySpan<short> stripe, ReadOnlySpan<short> previous, int i, int minValue, int maxValue)
    {
        Span<short> noise = stackalloc short[ChunkLength];
        Span<short> scale = stackalloc short[ChunkLength];
        for (var x0 = 0; x0 < sourceRow.Length; x0 += ChunkLength)
        {
            var count = Math.Min(ChunkLength, sourceRow.Length - x0);
            var source = sourceRow.Slice(x0, count);
            for (var k = 0; k < source.Length; k++)
            {
                scale[k] = scaling[source[k]];
            }

            AddNoise(source, GetNoiseChunk(stripe, previous, planeSubY: 0, i, x0, count, noise), scale[..count], destinationRow.Slice(x0, count), _parameters.GrainScaling, minValue, maxValue);
        }
    }

    // The vector path of a chroma row, by chunks: the merged value of every sample, its scaling, then the scaled noise
    private void ApplyChromaRow(ReadOnlySpan<ushort> sourceRow, ReadOnlySpan<ushort> lumaRow, Span<ushort> destinationRow, ReadOnlySpan<short> scaling, ReadOnlySpan<short> stripe, ReadOnlySpan<short> previous, int i, int lumaMult, int mult, int offset, int maxSample, int minValue, int maxValue)
    {
        Span<short> noise = stackalloc short[ChunkLength];
        Span<short> scale = stackalloc short[ChunkLength];
        Span<ushort> merged = stackalloc ushort[ChunkLength];
        var subX = _subX;
        var chromaScalingFromLuma = _parameters.ChromaScalingFromLuma;
        var lastLuma = _width - 1;

        // The samples whose 8 samples from them have their luma samples in the row (the last sample of a row of odd width
        // averages the last luma sample with itself)
        var vectorEnd = (_width >> subX) - 7;
        var lumaMultiplier = Vector128.Create(lumaMult);
        var multiplier = Vector128.Create(mult);
        var mergedOffset = Vector128.Create(offset);
        var maximum = Vector128.Create(maxSample);
        for (var x0 = 0; x0 < sourceRow.Length; x0 += ChunkLength)
        {
            var count = Math.Min(ChunkLength, sourceRow.Length - x0);
            var source = sourceRow.Slice(x0, count);
            var k = 0;
            for (; k < count - 7 && x0 + k < vectorEnd; k += 8)
            {
                Vector128<int> low;
                Vector128<int> high;
                if (subX is 1)
                {
                    // The average of the pairs of luma samples, from the low and high halves of their 32-bit lanes
                    var first = Vector128.Create(lumaRow.Slice((x0 + k) * 2, 8)).AsInt32();
                    var second = Vector128.Create(lumaRow.Slice(((x0 + k) * 2) + 8, 8)).AsInt32();
                    low = ((first & Vector128.Create(0xFFFF)) + (first >>> 16) + Vector128<int>.One) >> 1;
                    high = ((second & Vector128.Create(0xFFFF)) + (second >>> 16) + Vector128<int>.One) >> 1;
                }
                else
                {
                    var luma = Vector128.Create(lumaRow.Slice(x0 + k, 8));
                    low = Vector128.WidenLower(luma).AsInt32();
                    high = Vector128.WidenUpper(luma).AsInt32();
                }

                if (!chromaScalingFromLuma)
                {
                    var samples = Vector128.Create(source.Slice(k, 8));
                    low = Vector128.Min(Vector128.Max((((low * lumaMultiplier) + (Vector128.WidenLower(samples).AsInt32() * multiplier)) >> 6) + mergedOffset, Vector128<int>.Zero), maximum);
                    high = Vector128.Min(Vector128.Max((((high * lumaMultiplier) + (Vector128.WidenUpper(samples).AsInt32() * multiplier)) >> 6) + mergedOffset, Vector128<int>.Zero), maximum);
                }

                Vector128.Narrow(low.AsUInt32(), high.AsUInt32()).CopyTo(merged[k..]);
            }

            for (; k < count; k++)
            {
                var lumaX = (x0 + k) << subX;
                var averageLuma = subX is 1 ? (lumaRow[lumaX] + lumaRow[Math.Min(lumaX + 1, lastLuma)] + 1) >> 1 : lumaRow[lumaX];
                merged[k] = (ushort)(chromaScalingFromLuma ? averageLuma : Math.Clamp((((averageLuma * lumaMult) + (source[k] * mult)) >> 6) + offset, 0, maxSample));
            }

            for (k = 0; k < count; k++)
            {
                scale[k] = scaling[merged[k]];
            }

            AddNoise(source, GetNoiseChunk(stripe, previous, _subY, i, x0, count, noise), scale[..count], destinationRow.Slice(x0, count), _parameters.GrainScaling, minValue, maxValue);
        }
    }

    // GetNoise for the samples x0 to x0 + count - 1 of the row i of a stripe: the row of the noise stripe, or, for the rows
    // that overlap the previous stripe, their blend in the scratch
    private ReadOnlySpan<short> GetNoiseChunk(ReadOnlySpan<short> stripe, ReadOnlySpan<short> previous, int planeSubY, int i, int x0, int count, Span<short> scratch)
    {
        var row = stripe.Slice((i * _noiseStride) + x0, count);
        if (!_parameters.OverlapFlag || _currentStripe is 0 || i >= (2 >> planeSubY))
            return row;

        var old = previous.Slice(((i + (StripeHeight >> planeSubY)) * _noiseStride) + x0, count);
        var (oldWeight, weight) = planeSubY is not 0 ? (23, 22) : i is 0 ? (27, 17) : (17, 27);
        var blended = scratch[..count];
        for (var k = 0; k < blended.Length; k++)
        {
            blended[k] = (short)Math.Clamp(Round2((old[k] * oldWeight) + (row[k] * weight), 5), _grainMin, _grainMax);
        }

        return blended;
    }

    // destination = Clamp(source + Round2(scale * noise, shift), min, max), 8 samples at a time: the products fit 32 bits (the
    // scaling is below 256 and the noise at most 2^11 in magnitude), the scaled noise and the sums 16 bits
    private static void AddNoise(ReadOnlySpan<ushort> source, ReadOnlySpan<short> noise, ReadOnlySpan<short> scale, Span<ushort> destination, int shift, int min, int max)
    {
        var round = Vector128.Create(1 << (shift - 1));
        var minimum = Vector128.Create((short)min);
        var maximum = Vector128.Create((short)max);
        var x = 0;
        for (; x <= source.Length - 8; x += 8)
        {
            var scaling = Vector128.Create(scale.Slice(x, 8));
            var grain = Vector128.Create(noise.Slice(x, 8));
            var low = ((Vector128.WidenLower(scaling) * Vector128.WidenLower(grain)) + round) >> shift;
            var high = ((Vector128.WidenUpper(scaling) * Vector128.WidenUpper(grain)) + round) >> shift;
            var sum = Vector128.Create(source.Slice(x, 8)).AsInt16() + Vector128.Narrow(low, high);
            Vector128.Min(Vector128.Max(sum, minimum), maximum).AsUInt16().CopyTo(destination[x..]);
        }

        for (; x < source.Length; x++)
        {
            destination[x] = (ushort)Math.Clamp(source[x] + Round2(scale[x] * noise[x], shift), min, max);
        }
    }

    public void Dispose()
    {
        _buffer?.Dispose();
        _buffer = null;
    }
}
