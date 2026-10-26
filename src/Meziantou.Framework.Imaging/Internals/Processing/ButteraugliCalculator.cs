namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the Butteraugli distance of two frames of the same size (<see cref="ImageSimilarity.ComputeButteraugliDistance"/>):
/// a port of <c>lib/jxl/butteraugli/butteraugli.cc</c> of libjxl 0.12.0 (by Jyrki Alakuijala), with the parameters of
/// <c>butteraugli_main</c> for SDR images (<c>hf_asymmetry = 1</c>, <c>xmul = 1</c>, 80 nits).
/// </summary>
/// <remarks>
/// <para>
/// Both frames are converted to linear sRGB, then to the "opsin dynamics" XYB of Butteraugli, whose sensitivity depends on
/// the blurred image. Each one is split into low, medium, high and ultra-high frequencies; the differences of every band are
/// weighted (L2, asymmetric L2 and Malta line filters), masked by the high-frequency activity of both frames, and combined
/// into a map with one distance per pixel. The map of the frames downsampled by 2 is mixed in, as libjxl does. The result
/// is the largest value of the map and its 3-norm (the mean of the 3-, 6- and 12-norms, as <c>butteraugli_main</c> prints
/// it).
/// </para>
/// <para>
/// The planes are 32-bit floats, as in libjxl, computed with scalar arithmetic in the order of the scalar code of libjxl:
/// the results are within about 0.01% of the tool (2e-5 on the test images), which uses SIMD instructions, fused
/// multiply-adds and an approximation of the logarithm. Unlike SSIMULACRA 2, the map does not amplify the rounding. A frame smaller than 8×8 is extended to 8×8 by repeating its edges, as
/// <c>ButteraugliDiffmap</c> does (the tool reports 0 for such frames). A frame that is not fully opaque is composited over
/// black and over white, and every pixel of the map keeps the larger distance, as the libjxl comparator does.
/// </para>
/// <para>
/// About 30 planes of the size of the frame are live at the peak (120 bytes per pixel).
/// </para>
/// </remarks>
internal static class ButteraugliCalculator
{
    private const int MinimumSize = 8;
    private const float IntensityTarget = 80f;

    private const double WMfMalta = 37.0819870399;
    private const double Norm1Mf = 130262059.556;
    private const double WMfMaltaX = 8246.75321353;
    private const double Norm1MfX = 1009002.70582;
    private const double WHfMalta = 18.7237414387;
    private const double Norm1Hf = 4498534.45232;
    private const double WHfMaltaX = 6923.99476109;
    private const double Norm1HfX = 8051.15833247;
    private const double WUhfMalta = 1.10039032555;
    private const double Norm1Uhf = 71.7800275169;
    private const double WUhfMaltaX = 173.5;
    private const double Norm1UhfX = 5.0;

    // log(80) / log(255)
    private const float IntensityTargetNormalizationHack = 0.79079917404f;
    private const float InternalGoodQualityThreshold = 17.83f * IntensityTargetNormalizationHack;
    private const float GlobalScale = (float)(1.0 / InternalGoodQualityThreshold);

    private static readonly double[] Wmul =
    [
        400.0, 1.50815703118, 0,
        2150.0, 10.6195433239, 16.2176043152,
        29.2353797994, 0.844626970982, 0.703646627719,
    ];

    /// <exception cref="ImageResourceLimitException">The planes exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public static ButteraugliDistance Compute(AllocationScope scope, PixelSource expected, PixelSource actual, CancellationToken cancellationToken)
    {
        using var arena = new FloatPlaneArena(scope);
        var opaque = expected.IsOpaque(cancellationToken) && actual.IsOpaque(cancellationToken);
        var diffmap = ComputeDiffmap(arena, expected, actual, background: 0f, cancellationToken);
        if (!opaque)
        {
            var white = ComputeDiffmap(arena, expected, actual, background: 1f, cancellationToken);
            var values = diffmap.Span;
            var whiteValues = white.Span;
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = Math.Max(values[i], whiteValues[i]);
            }

            arena.Release(white);
        }

        return new ButteraugliDistance(ComputeMax(diffmap.Span), ComputePNorm3(diffmap.Span));
    }

    private static double ComputeMax(ReadOnlySpan<float> diffmap)
    {
        var max = 0f;
        foreach (var value in diffmap)
        {
            max = Math.Max(max, value);
        }

        return max;
    }

    private static double ComputePNorm3(ReadOnlySpan<float> diffmap)
    {
        const double P = 3;
        double sum3 = 0, sum6 = 0, sum12 = 0;
        foreach (var value in diffmap)
        {
            double d1 = value;
            var d = d1 * d1 * d1;
            sum3 += d;
            d *= d;
            sum6 += d;
            d *= d;
            sum12 += d;
        }

        var onePerPixels = 1.0 / diffmap.Length;
        var v = Math.Pow(onePerPixels * sum3, 1.0 / P) + Math.Pow(onePerPixels * sum6, 1.0 / (P * 2)) + Math.Pow(onePerPixels * sum12, 1.0 / (P * 4));
        return v / 3.0;
    }

    private static FloatPlane ComputeDiffmap(FloatPlaneArena arena, PixelSource expected, PixelSource actual, float background, CancellationToken cancellationToken)
    {
        var width = expected.Width;
        var height = expected.Height;
        var rgb0 = arena.Rent(3, width, height);
        var rgb1 = arena.Rent(3, width, height);
        LinearRgbLoader.Load(expected, background, rgb0, cancellationToken);
        LinearRgbLoader.Load(actual, background, rgb1, cancellationToken);
        if (width >= MinimumSize && height >= MinimumSize)
            return ComputeDiffmap(arena, rgb0, rgb1, cancellationToken);

        // ButteraugliDiffmapSmall: the frame is centered in an 8×8 (at least) frame that repeats its edges
        var scaledWidth = Math.Max(MinimumSize, width);
        var scaledHeight = Math.Max(MinimumSize, height);
        var xBorder = width < MinimumSize ? (MinimumSize - width) / 2 : 0;
        var yBorder = height < MinimumSize ? (MinimumSize - height) / 2 : 0;
        var scaled0 = Extend(arena, rgb0, scaledWidth, scaledHeight, xBorder, yBorder);
        var scaled1 = Extend(arena, rgb1, scaledWidth, scaledHeight, xBorder, yBorder);
        arena.Release(rgb0);
        arena.Release(rgb1);
        var scaledDiffmap = ComputeDiffmap(arena, scaled0, scaled1, cancellationToken);
        var diffmap = arena.Rent(width, height);
        for (var y = 0; y < height; y++)
        {
            scaledDiffmap.GetRow(y + yBorder).Slice(xBorder, width).CopyTo(diffmap.GetRow(y));
        }

        arena.Release(scaledDiffmap);
        return diffmap;

        static FloatPlane[] Extend(FloatPlaneArena arena, FloatPlane[] rgb, int scaledWidth, int scaledHeight, int xBorder, int yBorder)
        {
            var width = rgb[0].Width;
            var height = rgb[0].Height;
            var scaled = arena.Rent(3, scaledWidth, scaledHeight);
            for (var c = 0; c < 3; c++)
            {
                var source = rgb[c].Span;
                var destination = scaled[c].Span;
                for (var y = 0; y < scaledHeight; y++)
                {
                    var y2 = Math.Min(height - 1, y > yBorder ? y - yBorder : 0);
                    for (var x = 0; x < scaledWidth; x++)
                    {
                        var x2 = Math.Min(width - 1, x > xBorder ? x - xBorder : 0);
                        destination[y * scaledWidth + x] = source[y2 * width + x2];
                    }
                }
            }

            return scaled;
        }
    }

    /// <summary>ButteraugliComparator::Diffmap: the full-resolution map, mixed with the map of the frames downsampled by 2.</summary>
    private static FloatPlane ComputeDiffmap(FloatPlaneArena arena, FloatPlane[] rgb0, FloatPlane[] rgb1, CancellationToken cancellationToken)
    {
        var width = rgb0[0].Width;
        var height = rgb0[0].Height;
        FloatPlane[]? sub0 = null;
        FloatPlane[]? sub1 = null;
        if ((width + 1) / 2 >= MinimumSize && (height + 1) / 2 >= MinimumSize)
        {
            sub0 = SubSample2x(arena, rgb0);
            sub1 = SubSample2x(arena, rgb1);
        }

        var diffmap = ComputeOpsinDiffmap(arena, rgb0, rgb1, cancellationToken);
        if (sub0 is not null && sub1 is not null)
        {
            var subDiffmap = ComputeOpsinDiffmap(arena, sub0, sub1, cancellationToken);
            AddSupersampled2x(subDiffmap, 0.5f, diffmap);
            arena.Release(subDiffmap);
        }

        return diffmap;
    }

    /// <summary>Computes the map of one scale. The planes of both frames are released.</summary>
    private static FloatPlane ComputeOpsinDiffmap(FloatPlaneArena arena, FloatPlane[] rgb0, FloatPlane[] rgb1, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var xyb0 = OpsinDynamicsImage(arena, rgb0);
        arena.Release(rgb0);
        var pi0 = SeparateFrequencies(arena, xyb0);

        cancellationToken.ThrowIfCancellationRequested();
        var xyb1 = OpsinDynamicsImage(arena, rgb1);
        arena.Release(rgb1);
        var pi1 = SeparateFrequencies(arena, xyb1);

        cancellationToken.ThrowIfCancellationRequested();
        return DiffmapPsychoImage(arena, pi0, pi1);
    }

    private static FloatPlane[] SubSample2x(FloatPlaneArena arena, FloatPlane[] input)
    {
        var width = input[0].Width;
        var height = input[0].Height;
        var outWidth = (width + 1) / 2;
        var outHeight = (height + 1) / 2;
        var output = arena.Rent(3, outWidth, outHeight);
        for (var c = 0; c < 3; c++)
        {
            var source = input[c].Span;
            var destination = output[c].Span;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    destination[(y / 2) * outWidth + (x / 2)] += 0.25f * source[y * width + x];
                }
            }

            if ((width & 1) != 0)
            {
                for (var y = 0; y < outHeight; y++)
                {
                    destination[y * outWidth + outWidth - 1] *= 2.0f;
                }
            }

            if ((height & 1) != 0)
            {
                for (var x = 0; x < outWidth; x++)
                {
                    destination[(outHeight - 1) * outWidth + x] *= 2.0f;
                }
            }
        }

        return output;
    }

    private static void AddSupersampled2x(FloatPlane source, float weight, FloatPlane destination)
    {
        // There are fewer errors in the more averaged frames: a heuristic scaler accounts for it
        const double HeuristicMixingValue = 0.3;
        var width = destination.Width;
        var sourceWidth = source.Width;
        var sourceValues = source.Span;
        var values = destination.Span;
        for (var y = 0; y < destination.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                ref var value = ref values[y * width + x];
                value = (float)(value * (1.0 - HeuristicMixingValue * weight));
                value += weight * sourceValues[(y / 2) * sourceWidth + (x / 2)];
            }
        }
    }

    private static FloatPlane[] OpsinDynamicsImage(FloatPlaneArena arena, FloatPlane[] rgb)
    {
        const float Sigma = 1.2f;
        const float Min = 1e-4f;
        const float Min01 = 1.7557483643287353f;
        const float Min2 = 12.226454707163354f;

        var width = rgb[0].Width;
        var height = rgb[0].Height;
        var blurred = arena.Rent(3, width, height);
        for (var c = 0; c < 3; c++)
        {
            ButteraugliBlur.Blur(arena, rgb[c], Sigma, blurred[c]);
        }

        var xyb = arena.Rent(3, width, height);
        var red = rgb[0].Span;
        var green = rgb[1].Span;
        var blue = rgb[2].Span;
        var blurredRed = blurred[0].Span;
        var blurredGreen = blurred[1].Span;
        var blurredBlue = blurred[2].Span;
        var outX = xyb[0].Span;
        var outY = xyb[1].Span;
        var outB = xyb[2].Span;
        for (var i = 0; i < red.Length; i++)
        {
            // The sensitivity comes from the derivative of the gamma of the blurred frame
            OpsinAbsorbance(blurredRed[i] * IntensityTarget, blurredGreen[i] * IntensityTarget, blurredBlue[i] * IntensityTarget, clamp: true, out var preMixed0, out var preMixed1, out var preMixed2);
            preMixed0 = Math.Max(preMixed0, Min);
            preMixed1 = Math.Max(preMixed1, Min);
            preMixed2 = Math.Max(preMixed2, Min);
            var sensitivity0 = Math.Max(Gamma(preMixed0) / preMixed0, Min);
            var sensitivity1 = Math.Max(Gamma(preMixed1) / preMixed1, Min);
            var sensitivity2 = Math.Max(Gamma(preMixed2) / preMixed2, Min);

            OpsinAbsorbance(red[i] * IntensityTarget, green[i] * IntensityTarget, blue[i] * IntensityTarget, clamp: false, out var curMixed0, out var curMixed1, out var curMixed2);
            curMixed0 = Math.Max(curMixed0 * sensitivity0, Min01);
            curMixed1 = Math.Max(curMixed1 * sensitivity1, Min01);
            curMixed2 = Math.Max(curMixed2 * sensitivity2, Min2);

            outX[i] = curMixed0 - curMixed1;
            outY[i] = curMixed0 + curMixed1;
            outB[i] = curMixed2;
        }

        arena.Release(blurred);
        return xyb;
    }

    private static void OpsinAbsorbance(float in0, float in1, float in2, bool clamp, out float out0, out float out1, out float out2)
    {
        const float Mix0 = 0.29956550340058319f;
        const float Mix1 = 0.63373087833825936f;
        const float Mix2 = 0.077705617820981968f;
        const float Mix3 = 1.7557483643287353f;
        const float Mix4 = 0.22158691104574774f;
        const float Mix5 = 0.69391388044116142f;
        const float Mix6 = 0.0987313588422f;
        const float Mix7 = 1.7557483643287353f;
        const float Mix8 = 0.02f;
        const float Mix9 = 0.02f;
        const float Mix10 = 0.20480129041026129f;
        const float Mix11 = 12.226454707163354f;

        out0 = Mix0 * in0 + (Mix1 * in1 + (Mix2 * in2 + Mix3));
        out1 = Mix4 * in0 + (Mix5 * in1 + (Mix6 * in2 + Mix7));
        out2 = Mix8 * in0 + (Mix9 * in1 + (Mix10 * in2 + Mix11));
        if (clamp)
        {
            out0 = Math.Max(out0, Mix3);
            out1 = Math.Max(out1, Mix7);
            out2 = Math.Max(out2, Mix11);
        }
    }

    /// <summary>A simple HDR-compatible gamma: <c>19.245 ln(v + 9.971) − 23.160</c>.</summary>
    private static float Gamma(float v)
    {
        const float RetMul = 19.245013259874995f * 0.6931471805599453f;
        const float RetAdd = -23.16046239805755f;
        var biased = Math.Max(v, 0f) + 9.9710635769299145f;
        return RetMul * (float)Math.Log2(biased) + RetAdd;
    }

    private static PsychoImage SeparateFrequencies(FloatPlaneArena arena, FloatPlane[] xyb)
    {
        const float SigmaLf = 7.15593339443f;
        const float SigmaHf = 3.22489901262f;
        const float SigmaUhf = 1.56416327805f;

        var width = xyb[0].Width;
        var height = xyb[0].Height;

        // SeparateLFAndMF: the XYB planes become the medium frequencies
        var lf = arena.Rent(3, width, height);
        var mf = xyb;
        for (var i = 0; i < 3; i++)
        {
            ButteraugliBlur.Blur(arena, xyb[i], SigmaLf, lf[i]);
            Subtract(mf[i].Span, lf[i].Span);
        }

        XybLowFreqToVals(lf);

        // SeparateMFAndHF
        var hf = arena.Rent(2, width, height);
        for (var i = 0; i < 3; i++)
        {
            if (i is 2)
            {
                ButteraugliBlur.Blur(arena, mf[i], SigmaHf, mf[i]);
                break;
            }

            mf[i].Span.CopyTo(hf[i].Span);
            ButteraugliBlur.Blur(arena, mf[i], SigmaHf, mf[i]);
            var mfValues = mf[i].Span;
            var hfValues = hf[i].Span;
            for (var j = 0; j < mfValues.Length; j++)
            {
                hfValues[j] -= mfValues[j];
                mfValues[j] = i is 0 ? RemoveRangeAroundZero(0.29f, mfValues[j]) : AmplifyRangeAroundZero(0.1f, mfValues[j]);
            }
        }

        // Suppress red-green by intensity change in the high frequencies
        SuppressXByY(hf[1].Span, hf[0].Span);

        // SeparateHFAndUHF
        var uhf = arena.Rent(2, width, height);
        for (var i = 0; i < 2; i++)
        {
            hf[i].Span.CopyTo(uhf[i].Span);
            ButteraugliBlur.Blur(arena, hf[i], SigmaUhf, hf[i]);
            var hfValues = hf[i].Span;
            var uhfValues = uhf[i].Span;
            for (var j = 0; j < hfValues.Length; j++)
            {
                if (i is 0)
                {
                    var hfValue = hfValues[j];
                    var uhfValue = uhfValues[j] - hfValue;
                    hfValues[j] = RemoveRangeAroundZero(1.5f, hfValue);
                    uhfValues[j] = RemoveRangeAroundZero(0.04f, uhfValue);
                }
                else
                {
                    var hfValue = MaximumClamp(hfValues[j], 28.4691806922f);
                    var uhfValue = MaximumClamp(uhfValues[j] - hfValue, 5.19175294647f);
                    uhfValues[j] = uhfValue * 2.69313763794f;
                    hfValues[j] = AmplifyRangeAroundZero(0.132f, hfValue * 2.155f);
                }
            }
        }

        return new PsychoImage(lf, mf, hf, uhf);
    }

    private static void Subtract(Span<float> minuend, ReadOnlySpan<float> subtrahend)
    {
        for (var i = 0; i < minuend.Length; i++)
        {
            minuend[i] -= subtrahend[i];
        }
    }

    /// <summary>Converts the low-frequency XYB to the "vals" space, where a squared difference is a distance.</summary>
    private static void XybLowFreqToVals(FloatPlane[] lf)
    {
        const float XMul = 33.832837186260f;
        const float YMul = 14.458268100570f;
        const float BMul = 49.87984651440f;
        const float YToBMul = -0.362267051518f;
        var x = lf[0].Span;
        var y = lf[1].Span;
        var b = lf[2].Span;
        for (var i = 0; i < x.Length; i++)
        {
            b[i] = (YToBMul * y[i] + b[i]) * BMul;
            x[i] *= XMul;
            y[i] *= YMul;
        }
    }

    private static void SuppressXByY(ReadOnlySpan<float> y, Span<float> x)
    {
        const float Suppress = 46.0f;
        const float S = 0.653020556257f;
        const float OneMinusS = (float)(1.0 - 0.653020556257);
        for (var i = 0; i < x.Length; i++)
        {
            var scaler = Suppress / (y[i] * y[i] + Suppress) * OneMinusS + S;
            x[i] *= scaler;
        }
    }

    private static float MaximumClamp(float v, float maxValue)
    {
        const float Mul = 0.724216145665f;
        if (v >= maxValue)
            return (v - maxValue) * Mul + maxValue;

        if (v < -maxValue)
            return (v + maxValue) * Mul - maxValue;

        return v;
    }

    /// <summary>Makes the area around zero less important (removes it).</summary>
    private static float RemoveRangeAroundZero(float w, float x) => x > w ? x - w : x < -w ? x + w : 0f;

    /// <summary>Makes the area around zero more important (doubles it up to the limit).</summary>
    private static float AmplifyRangeAroundZero(float w, float x) => x > w ? x + w : x < -w ? x - w : x + x;

    private static FloatPlane DiffmapPsychoImage(FloatPlaneArena arena, PsychoImage pi0, PsychoImage pi1)
    {
        var width = pi0.Lf[0].Width;
        var height = pi0.Lf[0].Height;

        // The planes are released as soon as their last term is added; the terms of each sum keep the order of libjxl
        var blockDiffDc = arena.Rent(3, width, height);
        for (var c = 0; c < 3; c++)
        {
            SetL2Diff(pi0.Lf[c].Span, pi1.Lf[c].Span, (float)Wmul[6 + c], blockDiffDc[c].Span);
        }

        arena.Release(pi0.Lf);
        arena.Release(pi1.Lf);

        var mask0 = arena.Rent(width, height);
        var mask1 = arena.Rent(width, height);
        CombineChannelsForMasking(pi0.Hf, pi0.Uhf, mask0.Span);
        CombineChannelsForMasking(pi1.Hf, pi1.Uhf, mask1.Span);

        var blockDiffAc = arena.Rent(3, width, height);
        var padded = arena.Rent(width + 2 * ButteraugliMalta.Padding, height + 2 * ButteraugliMalta.Padding);
        ButteraugliMalta.AddDiffMap(lowFrequency: false, pi0.Uhf[1], pi1.Uhf[1], WUhfMalta, WUhfMalta, Norm1Uhf, padded, blockDiffAc[1]);
        ButteraugliMalta.AddDiffMap(lowFrequency: false, pi0.Uhf[0], pi1.Uhf[0], WUhfMaltaX, WUhfMaltaX, Norm1UhfX, padded, blockDiffAc[0]);
        arena.Release(pi0.Uhf);
        arena.Release(pi1.Uhf);
        ButteraugliMalta.AddDiffMap(lowFrequency: true, pi0.Hf[1], pi1.Hf[1], WHfMalta, WHfMalta, Norm1Hf, padded, blockDiffAc[1]);
        ButteraugliMalta.AddDiffMap(lowFrequency: true, pi0.Hf[0], pi1.Hf[0], WHfMaltaX, WHfMaltaX, Norm1HfX, padded, blockDiffAc[0]);
        ButteraugliMalta.AddDiffMap(lowFrequency: true, pi0.Mf[1], pi1.Mf[1], WMfMalta, WMfMalta, Norm1Mf, padded, blockDiffAc[1]);
        ButteraugliMalta.AddDiffMap(lowFrequency: true, pi0.Mf[0], pi1.Mf[0], WMfMaltaX, WMfMaltaX, Norm1MfX, padded, blockDiffAc[0]);
        arena.Release(padded);

        for (var c = 0; c < 3; c++)
        {
            // No blue channel error accumulated at high frequencies
            if (c < 2)
            {
                L2DiffAsymmetric(pi0.Hf[c].Span, pi1.Hf[c].Span, (float)Wmul[c], (float)Wmul[c], blockDiffAc[c].Span);
            }

            L2Diff(pi0.Mf[c].Span, pi1.Mf[c].Span, (float)Wmul[3 + c], blockDiffAc[c].Span);
        }

        arena.Release(pi0.Hf);
        arena.Release(pi1.Hf);
        arena.Release(pi0.Mf);
        arena.Release(pi1.Mf);

        var mask = Mask(arena, mask0, mask1, blockDiffAc[1].Span);
        var diffmap = arena.Rent(width, height);
        CombineChannelsToDiffmap(mask.Span, blockDiffDc, blockDiffAc, diffmap.Span);
        arena.Release(mask);
        arena.Release(blockDiffDc);
        arena.Release(blockDiffAc);
        return diffmap;
    }

    private static void L2Diff(ReadOnlySpan<float> i0, ReadOnlySpan<float> i1, float w, Span<float> diffmap)
    {
        for (var i = 0; i < i0.Length; i++)
        {
            var diff = i0[i] - i1[i];
            diffmap[i] += diff * diff * w;
        }
    }

    private static void SetL2Diff(ReadOnlySpan<float> i0, ReadOnlySpan<float> i1, float w, Span<float> diffmap)
    {
        for (var i = 0; i < i0.Length; i++)
        {
            var diff = i0[i] - i1[i];
            diffmap[i] = diff * diff * w;
        }
    }

    /// <summary>The weighted L2 difference, plus a penalty when the distorted value is much smaller or larger than the reference.</summary>
    private static void L2DiffAsymmetric(ReadOnlySpan<float> i0, ReadOnlySpan<float> i1, float w0Gt1, float w0Lt1, Span<float> diffmap)
    {
        var vw0Gt1 = (float)(w0Gt1 * 0.8);
        var vw0Lt1 = (float)(w0Lt1 * 0.8);
        for (var i = 0; i < i0.Length; i++)
        {
            var val0 = i0[i];
            var val1 = i1[i];

            // Primary symmetric quadratic objective
            var diff = val0 - val1;
            var total = diff * diff * vw0Gt1 + diffmap[i];

            // Secondary half-open quadratic objectives
            var fabs0 = Math.Abs(val0);
            var tooSmall = 0.4f * fabs0;
            var tooBig = fabs0;
            float v;
            if (val0 < 0)
            {
                v = val1 > -tooSmall ? val1 + tooSmall : val1 < -tooBig ? -val1 - tooBig : 0f;
            }
            else
            {
                v = val1 < tooSmall ? tooSmall - val1 : val1 > tooBig ? val1 - tooBig : 0f;
            }

            diffmap[i] = vw0Lt1 * (v * v) + total;
        }
    }

    private static void CombineChannelsForMasking(FloatPlane[] hf, FloatPlane[] uhf, Span<float> output)
    {
        // Only X and Y are involved in masking: B is less important at high frequencies
        const float Mul0 = 2.5f;
        const float Mul1 = 0.4f;
        const float Mul2 = 0.4f;
        var yHf = hf[1].Span;
        var yUhf = uhf[1].Span;
        var xHf = hf[0].Span;
        var xUhf = uhf[0].Span;
        for (var i = 0; i < output.Length; i++)
        {
            var xDiff = (xUhf[i] + xHf[i]) * Mul0;
            var yDiff = yUhf[i] * Mul1 + yHf[i] * Mul2;
            output[i] = MathF.Sqrt(xDiff * xDiff + yDiff * yDiff);
        }
    }

    /// <summary>Computes the masking from the activity of both frames, and adds the difference of the masks to <paramref name="diffAc"/>.</summary>
    private static FloatPlane Mask(FloatPlaneArena arena, FloatPlane mask0, FloatPlane mask1, Span<float> diffAc)
    {
        const float Mul = 6.19424080439f;
        const float Bias = 12.61050594197f;
        const float Radius = 2.7f;
        const float MaskToErrorMul = 10.0f;

        var width = mask0.Width;
        var height = mask0.Height;
        DiffPrecompute(mask0.Span, Mul, Bias);
        DiffPrecompute(mask1.Span, Mul, Bias);
        var blurred0 = arena.Rent(width, height);
        var blurred1 = arena.Rent(width, height);
        ButteraugliBlur.Blur(arena, mask0, Radius, blurred0);
        FuzzyErosion(blurred0.Span, mask0.Span, width, height);
        ButteraugliBlur.Blur(arena, mask1, Radius, blurred1);
        var values0 = blurred0.Span;
        var values1 = blurred1.Span;
        for (var i = 0; i < diffAc.Length; i++)
        {
            var diff = values0[i] - values1[i];
            diffAc[i] += MaskToErrorMul * diff * diff;
        }

        arena.Release(blurred0);
        arena.Release(blurred1);
        arena.Release(mask1);
        return mask0;
    }

    private static void DiffPrecompute(Span<float> values, float mul, float biasArgument)
    {
        // The bias makes the square root behave more linearly
        var bias = mul * biasArgument;
        var sqrtBias = MathF.Sqrt(bias);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = MathF.Sqrt(mul * Math.Abs(values[i]) + bias) - sqrtBias;
        }
    }

    /// <summary>Looks for smooth areas near the area of degradation: if they are generally smooth, there is no masking.</summary>
    private static void FuzzyErosion(ReadOnlySpan<float> from, Span<float> to, int width, int height)
    {
        const int Step = 3;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var min0 = from[y * width + x];
                var min1 = 2 * min0;
                var min2 = min1;
                if (x >= Step)
                {
                    StoreMin3(from[y * width + x - Step], ref min0, ref min1, ref min2);
                    if (y >= Step)
                    {
                        StoreMin3(from[(y - Step) * width + x - Step], ref min0, ref min1, ref min2);
                    }

                    if (y < height - Step)
                    {
                        StoreMin3(from[(y + Step) * width + x - Step], ref min0, ref min1, ref min2);
                    }
                }

                if (x < width - Step)
                {
                    StoreMin3(from[y * width + x + Step], ref min0, ref min1, ref min2);
                    if (y >= Step)
                    {
                        StoreMin3(from[(y - Step) * width + x + Step], ref min0, ref min1, ref min2);
                    }

                    if (y < height - Step)
                    {
                        StoreMin3(from[(y + Step) * width + x + Step], ref min0, ref min1, ref min2);
                    }
                }

                if (y >= Step)
                {
                    StoreMin3(from[(y - Step) * width + x], ref min0, ref min1, ref min2);
                }

                if (y < height - Step)
                {
                    StoreMin3(from[(y + Step) * width + x], ref min0, ref min1, ref min2);
                }

                to[y * width + x] = 0.45f * min0 + 0.3f * min1 + 0.25f * min2;
            }
        }
    }

    private static void StoreMin3(float v, ref float min0, ref float min1, ref float min2)
    {
        if (v < min2)
        {
            if (v < min0)
            {
                min2 = min1;
                min1 = min0;
                min0 = v;
            }
            else if (v < min1)
            {
                min2 = min1;
                min1 = v;
            }
            else
            {
                min2 = v;
            }
        }
    }

    private static double MaskY(double delta)
    {
        const double Offset = 0.829591754942;
        const double Scaler = 0.451936922203;
        const double Mul = 2.5485944793;
        var c = Mul / ((Scaler * delta) + Offset);
        var retval = GlobalScale * (1.0 + c);
        return retval * retval;
    }

    private static double MaskDcY(double delta)
    {
        const double Offset = 0.20025578522;
        const double Scaler = 3.87449418804;
        const double Mul = 0.505054525019;
        var c = Mul / ((Scaler * delta) + Offset);
        var retval = GlobalScale * (1.0 + c);
        return retval * retval;
    }

    private static void CombineChannelsToDiffmap(ReadOnlySpan<float> mask, FloatPlane[] blockDiffDc, FloatPlane[] blockDiffAc, Span<float> result)
    {
        var dc0 = blockDiffDc[0].Span;
        var dc1 = blockDiffDc[1].Span;
        var dc2 = blockDiffDc[2].Span;
        var ac0 = blockDiffAc[0].Span;
        var ac1 = blockDiffAc[1].Span;
        var ac2 = blockDiffAc[2].Span;
        for (var i = 0; i < result.Length; i++)
        {
            var value = mask[i];
            var maskValue = (float)MaskY(value);
            var dcMaskValue = (float)MaskDcY(value);
            var dc = dc0[i] * dcMaskValue + dc1[i] * dcMaskValue + dc2[i] * dcMaskValue;
            var ac = ac0[i] * maskValue + ac1[i] * maskValue + ac2[i] * maskValue;
            result[i] = MathF.Sqrt(dc + ac);
        }
    }

    /// <summary>The frequency bands of a frame: low (3 planes, in "vals" space), medium (3), high and ultra-high (X and Y).</summary>
    private sealed record PsychoImage(FloatPlane[] Lf, FloatPlane[] Mf, FloatPlane[] Hf, FloatPlane[] Uhf);
}
