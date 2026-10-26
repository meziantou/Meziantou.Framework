namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Computes the SSIMULACRA 2 score of two frames of the same size (<see cref="ImageSimilarity.ComputeSsimulacra2"/>): a
/// port of <c>tools/ssimulacra2.cc</c> of libjxl 0.12.0, by Jon Sneyers (Cloudinary).
/// </summary>
/// <remarks>
/// <para>
/// Both frames are converted to linear sRGB, then to the XYB color space of JPEG XL, rescaled to about [0, 1]. At 6 scales
/// (1:1 to 1:32, each one downsampled by 2 in linear sRGB), every channel gives three error maps: an SSIM map without the
/// luminance denominator, an "artifact" map (the distorted frame has edges where the reference is smooth) and a "detail
/// lost" map (the reverse). The 1-norm and the 4-norm of the 54 maps are weighted by the 108 constants of the tool, and the
/// sum is mapped to a score: <c>100</c> for identical frames, about <c>90</c> for visually lossless, <c>50</c> for a
/// medium quality, and negative values for very different frames.
/// </para>
/// <para>
/// The planes are 32-bit floats, as in libjxl. The local means and variances use the recursive Gaussian filter of libjxl
/// (sigma 1.5, radius 5). The SSIM map subtracts nearly equal blurred values, so the rounding of the blur shows in the
/// score (by up to 0.07 on a 1024×1024 frame): the blur, the cube root of the XYB conversion and the sRGB curve
/// (<see cref="LinearRgbLoader"/>) emulate the SIMD code of libjxl operation by operation, with fused multiply-adds, and
/// the scores agree with the 8 decimals the tool prints. Like the tool, a scale is computed when the previous one is at least 8×8, so the last
/// scale can be smaller than 8×8; the frames must be at least 8×8.
/// </para>
/// <para>
/// A frame that is not fully opaque is composited over a dark (0.1) and a bright (0.9) gray background in encoded sRGB,
/// and the result is the lower of the two scores, as the tool does for an image with alpha.
/// </para>
/// </remarks>
internal static class Ssimulacra2Calculator
{
    /// <summary>The minimum width and height of a frame.</summary>
    public const int MinimumSize = 8;

    private const int ScaleCount = 6;
    private const float C2 = 0.0009f;

    // jxl::cms opsin absorbance matrix and bias (lib/jxl/cms/opsin_params.h)
    private const float M00 = 0.30f;
    private const float M02 = 0.078f;
    private const float M01 = 1.0f - M02 - M00;
    private const float M12 = 0.078f;
    private const float M10 = 0.23f;
    private const float M11 = 1.0f - M12 - M10;
    private const float M20 = 0.24342268924547819f;
    private const float M21 = 0.20476744424496821f;
    private const float M22 = 1.0f - M20 - M21;
    private const float OpsinBias = 0.0037930732552754493f;

    private static readonly float NegativeCubeRootBias = -(float)Math.Cbrt(OpsinBias);

    private static readonly RecursiveGaussian Gaussian = new(1.5);

    private static readonly double[] Weights =
    [
        0.0, 0.0007376606707406586, 0.0, 0.0, 0.0007793481682867309, 0.0, 0.0, 0.0004371155730107379, 0.0,
        1.1041726426657346, 0.00066284834129271, 0.00015231632783718752, 0.0, 0.0016406437456599754, 0.0,
        1.8422455520539298, 11.441172603757666, 0.0, 0.0007989109436015163, 0.000176816438078653, 0.0,
        1.8787594979546387, 10.94906990605142, 0.0, 0.0007289346991508072, 0.9677937080626833, 0.0,
        0.00014003424285435884, 0.9981766977854967, 0.00031949755934435053, 0.0004550992113792063, 0.0, 0.0,
        0.0013648766163243398, 0.0, 0.0, 0.0, 0.0, 0.0, 7.466890328078848, 0.0, 17.445833984131262,
        0.0006235601634041466, 0.0, 0.0, 6.683678146179332, 0.00037724407979611296, 1.027889937768264,
        225.20515300849274, 0.0, 0.0, 19.213238186143016, 0.0011401524586618361, 0.001237755635509985,
        176.39317598450694, 0.0, 0.0, 24.43300999870476, 0.28520802612117757, 0.0004485436923833408, 0.0, 0.0,
        0.0, 34.77906344483772, 44.835625328877896, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0,
        0.0008680556573291698, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0005313191874358747, 0.0, 0.00016533814161379112, 0.0,
        0.0, 0.0, 0.0, 0.0, 0.0004179171803251336, 0.0017290828234722833, 0.0, 0.0020827005846636437, 0.0, 0.0,
        8.826982764996862, 23.19243343998926, 0.0, 95.1080498811086, 0.9863978034400682, 0.9834382792465353,
        0.0012286405048278493, 171.2667255897307, 0.9807858872435379, 0.0, 0.0, 0.0, 0.0005130064588990679, 0.0,
        0.00010854057858411537,
    ];

    /// <exception cref="ImageResourceLimitException">The planes exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The token was canceled.</exception>
    public static double Compute(AllocationScope scope, PixelSource expected, PixelSource actual, CancellationToken cancellationToken)
    {
        if (expected.IsOpaque(cancellationToken) && actual.IsOpaque(cancellationToken))
            return ComputeScore(scope, expected, actual, background: 0.5f, cancellationToken);

        var dark = ComputeScore(scope, expected, actual, background: 0.1f, cancellationToken);
        var bright = ComputeScore(scope, expected, actual, background: 0.9f, cancellationToken);
        return Math.Min(dark, bright);
    }

    private static double ComputeScore(AllocationScope scope, PixelSource expected, PixelSource actual, float background, CancellationToken cancellationToken)
    {
        using var arena = new FloatPlaneArena(scope);
        var width = expected.Width;
        var height = expected.Height;
        var reference = arena.Rent(3, width, height);
        var distorted = arena.Rent(3, width, height);
        LinearRgbLoader.Load(expected, background, reference, cancellationToken);
        LinearRgbLoader.Load(actual, background, distorted, cancellationToken);

        // Per scale: the 1- and 4-norms of the SSIM map (c × 2 + n), and of the artifact and detail-lost maps (c × 4 + ...)
        var ssimNorms = new List<double[]>(ScaleCount);
        var edgeNorms = new List<double[]>(ScaleCount);
        for (var scale = 0; scale < ScaleCount; scale++)
        {
            // The tool tests the size of the previous scale, so the last scale computed can be smaller than 8×8
            if (width < MinimumSize || height < MinimumSize)
                break;

            if (scale > 0)
            {
                var downsampledReference = Downsample(arena, reference);
                var downsampledDistorted = Downsample(arena, distorted);
                arena.Release(reference);
                arena.Release(distorted);
                reference = downsampledReference;
                distorted = downsampledDistorted;
                width = reference[0].Width;
                height = reference[0].Height;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var ssim = new double[6];
            var edges = new double[12];
            ComputeScale(arena, reference, distorted, ssim, edges, cancellationToken);
            ssimNorms.Add(ssim);
            edgeNorms.Add(edges);
        }

        return ComputeFinalScore(ssimNorms, edgeNorms);
    }

    private static void ComputeScale(FloatPlaneArena arena, FloatPlane[] reference, FloatPlane[] distorted, double[] ssimNorms, double[] edgeNorms, CancellationToken cancellationToken)
    {
        var width = reference[0].Width;
        var height = reference[0].Height;
        var image1 = arena.Rent(3, width, height);
        var image2 = arena.Rent(3, width, height);
        ToPositiveXyb(reference, image1);
        ToPositiveXyb(distorted, image2);

        var product = arena.Rent(width, height);
        var temp = arena.Rent(width, height);
        var state = arena.Rent(width, RecursiveGaussian.StateRows);
        var sigma11 = arena.Rent(width, height);
        var sigma22 = arena.Rent(width, height);
        var sigma12 = arena.Rent(width, height);
        var mu1 = arena.Rent(width, height);
        var mu2 = arena.Rent(width, height);
        for (var channel = 0; channel < 3; channel++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var plane1 = image1[channel].Span;
            var plane2 = image2[channel].Span;
            Multiply(plane1, plane1, product.Span);
            Gaussian.Blur(product.Span, temp.Span, sigma11.Span, state.Span, width, height);
            Multiply(plane2, plane2, product.Span);
            Gaussian.Blur(product.Span, temp.Span, sigma22.Span, state.Span, width, height);
            Multiply(plane1, plane2, product.Span);
            Gaussian.Blur(product.Span, temp.Span, sigma12.Span, state.Span, width, height);
            Gaussian.Blur(plane1, temp.Span, mu1.Span, state.Span, width, height);
            Gaussian.Blur(plane2, temp.Span, mu2.Span, state.Span, width, height);

            ComputeSsimMap(mu1.Span, mu2.Span, sigma11.Span, sigma22.Span, sigma12.Span, ssimNorms.AsSpan(channel * 2, 2));
            ComputeEdgeDiffMap(plane1, mu1.Span, plane2, mu2.Span, edgeNorms.AsSpan(channel * 4, 4));
        }

        arena.Release(image1);
        arena.Release(image2);
        arena.Release([product, temp, state, sigma11, sigma22, sigma12, mu1, mu2]);
    }

    private static double ComputeFinalScore(List<double[]> ssimNorms, List<double[]> edgeNorms)
    {
        // The weights are consumed in order: with fewer than 6 scales, the indices shift, as in the tool
        var sum = 0d;
        var i = 0;
        for (var channel = 0; channel < 3; channel++)
        {
            for (var scale = 0; scale < ssimNorms.Count; scale++)
            {
                for (var n = 0; n < 2; n++)
                {
                    sum += Weights[i++] * Math.Abs(ssimNorms[scale][channel * 2 + n]);
                    sum += Weights[i++] * Math.Abs(edgeNorms[scale][channel * 4 + n]);
                    sum += Weights[i++] * Math.Abs(edgeNorms[scale][channel * 4 + n + 2]);
                }
            }
        }

        sum *= 0.9562382616834844;
        sum = 2.326765642916932 * sum - 0.020884521182843837 * sum * sum + 6.248496625763138e-05 * sum * sum * sum;
        return sum > 0 ? 100.0 - 10.0 * Math.Pow(sum, 0.6276336467831387) : 100.0;
    }

    private static FloatPlane[] Downsample(FloatPlaneArena arena, FloatPlane[] input)
    {
        var inWidth = input[0].Width;
        var inHeight = input[0].Height;
        var outWidth = (inWidth + 1) / 2;
        var outHeight = (inHeight + 1) / 2;
        var output = arena.Rent(3, outWidth, outHeight);
        for (var c = 0; c < 3; c++)
        {
            var source = input[c].Span;
            var destination = output[c].Span;
            for (var oy = 0; oy < outHeight; oy++)
            {
                for (var ox = 0; ox < outWidth; ox++)
                {
                    var sum = 0f;
                    for (var iy = 0; iy < 2; iy++)
                    {
                        for (var ix = 0; ix < 2; ix++)
                        {
                            var x = Math.Min(ox * 2 + ix, inWidth - 1);
                            var y = Math.Min(oy * 2 + iy, inHeight - 1);
                            sum += source[y * inWidth + x];
                        }
                    }

                    destination[oy * outWidth + ox] = sum * 0.25f;
                }
            }
        }

        return output;
    }

    /// <summary>Converts linear sRGB to XYB (enc_xyb.cc, intensity target 255), then rescales it (MakePositiveXYB).</summary>
    private static void ToPositiveXyb(FloatPlane[] linear, FloatPlane[] xyb)
    {
        var red = linear[0].Span;
        var green = linear[1].Span;
        var blue = linear[2].Span;
        var outX = xyb[0].Span;
        var outY = xyb[1].Span;
        var outB = xyb[2].Span;
        for (var i = 0; i < red.Length; i++)
        {
            var r = red[i];
            var g = green[i];
            var b = blue[i];
            var mixed0 = CubeRootAndAdd(MathF.FusedMultiplyAdd(M00, r, MathF.FusedMultiplyAdd(M01, g, MathF.FusedMultiplyAdd(M02, b, OpsinBias))));
            var mixed1 = CubeRootAndAdd(MathF.FusedMultiplyAdd(M10, r, MathF.FusedMultiplyAdd(M11, g, MathF.FusedMultiplyAdd(M12, b, OpsinBias))));
            var mixed2 = CubeRootAndAdd(MathF.FusedMultiplyAdd(M20, r, MathF.FusedMultiplyAdd(M21, g, MathF.FusedMultiplyAdd(M22, b, OpsinBias))));
            var x = 0.5f * (mixed0 - mixed1);
            var y = 0.5f * (mixed0 + mixed1);
            outB[i] = (mixed2 - y) + 0.55f;
            outX[i] = x * 14f + 0.42f;
            outY[i] = y + 0.01f;
        }
    }

    /// <summary>
    /// The cube root of libjxl (<c>CubeRootAndAdd</c> in <c>lib/jxl/base/fast_math-inl.h</c>), plus the negative cube root
    /// of the bias: an estimate of <c>x^(-1/3)</c> from the exponent, refined by Newton's method with fused multiply-adds.
    /// </summary>
    private static float CubeRootAndAdd(float mixed)
    {
        const float OneThird = 1.0f / 3;
        const float FourThirds = 4.0f / 3;

        // Negative inputs (out of gamut) are clamped to zero
        var x = Math.Max(mixed, 0f);
        var xThird = OneThird * x;
        var bits = BitConverter.SingleToInt32Bits(x);
        var estimate = bits is 0 ? 0 : unchecked(0x54800000 - (bits >> 23) * 0x002AAAAA);
        var r = BitConverter.Int32BitsToSingle(estimate);
        for (var i = 0; i < 3; i++)
        {
            var r2 = r * r;
            r = MathF.FusedMultiplyAdd(-xThird, r2 * r2, FourThirds * r);
        }

        var square = r * r;
        r = MathF.FusedMultiplyAdd(OneThird, MathF.FusedMultiplyAdd(-x, square * square, r), r);
        square = r * r;
        return MathF.FusedMultiplyAdd(square, x, NegativeCubeRootBias);
    }

    private static void Multiply(ReadOnlySpan<float> a, ReadOnlySpan<float> b, Span<float> product)
    {
        for (var i = 0; i < product.Length; i++)
        {
            product[i] = a[i] * b[i];
        }
    }

    private static void ComputeSsimMap(ReadOnlySpan<float> m1, ReadOnlySpan<float> m2, ReadOnlySpan<float> s11, ReadOnlySpan<float> s22, ReadOnlySpan<float> s12, Span<double> norms)
    {
        var sum1 = 0d;
        var sum4 = 0d;
        for (var i = 0; i < m1.Length; i++)
        {
            var mu1 = m1[i];
            var mu2 = m2[i];
            var mu11 = mu1 * mu1;
            var mu22 = mu2 * mu2;
            var mu12 = mu1 * mu2;

            // The luminance term drops the denominator of SSIM: the values are already perceptually uniform
            var numM = (float)(1.0 - (mu1 - mu2) * (mu1 - mu2));
            var numS = 2 * (s12[i] - mu12) + C2;
            var denomS = (s11[i] - mu11) + (s22[i] - mu22) + C2;
            var d = Math.Max(1.0 - (numM * numS / denomS), 0.0);
            sum1 += d;
            sum4 += Quartic(d);
        }

        var onePerPixels = 1.0 / m1.Length;
        norms[0] = onePerPixels * sum1;
        norms[1] = Math.Sqrt(Math.Sqrt(onePerPixels * sum4));
    }

    private static void ComputeEdgeDiffMap(ReadOnlySpan<float> image1, ReadOnlySpan<float> mu1, ReadOnlySpan<float> image2, ReadOnlySpan<float> mu2, Span<double> norms)
    {
        var artifact1 = 0d;
        var artifact4 = 0d;
        var detailLost1 = 0d;
        var detailLost4 = 0d;
        for (var i = 0; i < image1.Length; i++)
        {
            var d1 = (1.0 + Math.Abs(image2[i] - mu2[i])) / (1.0 + Math.Abs(image1[i] - mu1[i])) - 1.0;

            // d1 > 0: the distorted frame has an edge where the reference is smooth (ringing, banding, blockiness)
            var artifact = Math.Max(d1, 0.0);
            artifact1 += artifact;
            artifact4 += Quartic(artifact);

            // d1 < 0: the reference has an edge where the distorted frame is smooth (blurring, smearing)
            var detailLost = Math.Max(-d1, 0.0);
            detailLost1 += detailLost;
            detailLost4 += Quartic(detailLost);
        }

        var onePerPixels = 1.0 / image1.Length;
        norms[0] = onePerPixels * artifact1;
        norms[1] = Math.Sqrt(Math.Sqrt(onePerPixels * artifact4));
        norms[2] = onePerPixels * detailLost1;
        norms[3] = Math.Sqrt(Math.Sqrt(onePerPixels * detailLost4));
    }

    private static double Quartic(double x)
    {
        x *= x;
        x *= x;
        return x;
    }

    /// <summary>
    /// The recursive approximation of a Gaussian blur of libjxl (<c>tools/gauss_blur.cc</c>, after Charalampidis 2016,
    /// "Recursive Implementation of the Gaussian Filter Using Truncated Cosine Functions"): three second-order filters per
    /// direction, with zeros outside the plane.
    /// </summary>
    private sealed class RecursiveGaussian
    {
        /// <summary>The rows of the state of the vertical pass: the last two outputs of each of the three filters.</summary>
        public const int StateRows = 6;

        private readonly int _radius;
        private readonly float[] _n2 = new float[3];
        private readonly float[] _d1 = new float[3];

        // The coefficients of four consecutive outputs, for the blocks of 4 samples of the horizontal pass (4 per filter)
        private readonly float[] _mulIn = new float[12];
        private readonly float[] _mulPrev = new float[12];
        private readonly float[] _mulPrev2 = new float[12];

        public RecursiveGaussian(double sigma)
        {
            var radius = Math.Round(3.2795 * sigma + 0.2546, MidpointRounding.AwayFromZero);
            var piDiv2R = Math.PI / (2.0 * radius);
            double[] omega = [piDiv2R, 3.0 * piDiv2R, 5.0 * piDiv2R];

            var p1 = +1.0 / Math.Tan(0.5 * omega[0]);
            var p3 = -1.0 / Math.Tan(0.5 * omega[1]);
            var p5 = +1.0 / Math.Tan(0.5 * omega[2]);

            var r1 = +p1 * p1 / Math.Sin(omega[0]);
            var r3 = -p3 * p3 / Math.Sin(omega[1]);
            var r5 = +p5 * p5 / Math.Sin(omega[2]);

            var negHalfSigma2 = -0.5 * sigma * sigma;
            var recipRadius = 1.0 / radius;
            var rho = new double[3];
            for (var i = 0; i < 3; i++)
            {
                rho[i] = Math.Exp(negHalfSigma2 * omega[i] * omega[i]) * recipRadius;
            }

            var d13 = p1 * r3 - r1 * p3;
            var d35 = p3 * r5 - r3 * p5;
            var d51 = p5 * r1 - r5 * p1;

            var recipD13 = 1.0 / d13;
            var zeta15 = d35 * recipD13;
            var zeta35 = d51 * recipD13;

            var inverse = Invert3x3(
            [
                p1, p3, p5,
                r1, r3, r5,
                zeta15, zeta35, 1,
            ]);
            double[] gamma = [1, radius * radius - sigma * sigma, zeta15 * rho[0] + zeta35 * rho[1] + rho[2]];

            _radius = (int)radius;
            for (var i = 0; i < 3; i++)
            {
                var beta = inverse[i * 3] * gamma[0] + inverse[i * 3 + 1] * gamma[1] + inverse[i * 3 + 2] * gamma[2];
                var n2 = -beta * Math.Cos(omega[i] * (radius + 1.0));
                var d1 = -2.0 * Math.Cos(omega[i]);
                _n2[i] = (float)n2;
                _d1[i] = (float)d1;

                // Four consecutive outputs of o(n) = n2 i(n) - d1 o(n - 1) - o(n - 2), expanded
                var d2 = d1 * d1;
                _mulPrev[4 * i] = (float)-d1;
                _mulPrev[4 * i + 1] = (float)(d2 - 1.0);
                _mulPrev[4 * i + 2] = (float)(-d2 * d1 + 2.0 * d1);
                _mulPrev[4 * i + 3] = (float)(d2 * d2 - 3.0 * d2 + 1.0);
                _mulPrev2[4 * i] = -1.0f;
                _mulPrev2[4 * i + 1] = (float)d1;
                _mulPrev2[4 * i + 2] = (float)(-d2 + 1.0);
                _mulPrev2[4 * i + 3] = (float)(d2 * d1 - 2.0 * d1);
                _mulIn[4 * i] = (float)n2;
                _mulIn[4 * i + 1] = (float)(-d1 * n2);
                _mulIn[4 * i + 2] = (float)(d2 * n2 - n2);
                _mulIn[4 * i + 3] = (float)(-d2 * d1 * n2 + 2.0 * d1 * n2);
            }
        }

        /// <summary>Blurs <paramref name="input"/> into <paramref name="output"/>; <paramref name="temp"/> has the size of the plane.</summary>
        public void Blur(ReadOnlySpan<float> input, Span<float> temp, Span<float> output, Span<float> state, int width, int height)
        {
            for (var y = 0; y < height; y++)
            {
                BlurRow(input.Slice(y * width, width), temp.Slice(y * width, width));
            }

            BlurColumns(temp, output, state, width, height);
        }

        /// <summary>
        /// The horizontal pass of libjxl (<c>FastGaussian1D</c>, 4 lanes), emulated lane by lane with fused multiply-adds: one
        /// sample at a time near the borders, and blocks of 4 samples between them.
        /// </summary>
        private void BlurRow(ReadOnlySpan<float> input, Span<float> output)
        {
            const int Lanes = 4;
            var length = input.Length;
            var radius = _radius;
            Span<float> prev = stackalloc float[3];
            Span<float> prev2 = stackalloc float[3];
            Span<float> block = stackalloc float[Lanes * 3];

            var n = -radius + 1;
            var firstAligned = (radius + 1 + Lanes - 1) / Lanes * Lanes;
            for (; n < Math.Min(firstAligned, length); n++)
            {
                StepOne(input, output, n, prev, prev2);
            }

            for (; n < length - radius + 1 - (Lanes - 1); n += Lanes)
            {
                for (var k = 0; k < 3; k++)
                {
                    for (var lane = 0; lane < Lanes; lane++)
                    {
                        // out = Σ_j shifted(mul_in, j)[lane] × sum[j], then the terms of the previous outputs
                        var value = (input[n - radius - 1] + input[n + radius - 1]) * _mulIn[4 * k + lane];
                        for (var j = 1; j <= lane; j++)
                        {
                            value = MathF.FusedMultiplyAdd(_mulIn[4 * k + lane - j], input[n + j - radius - 1] + input[n + j + radius - 1], value);
                        }

                        value = MathF.FusedMultiplyAdd(_mulPrev2[4 * k + lane], prev2[k], value);
                        value = MathF.FusedMultiplyAdd(_mulPrev[4 * k + lane], prev[k], value);
                        block[k * Lanes + lane] = value;
                    }

                    prev2[k] = block[k * Lanes + Lanes - 2];
                    prev[k] = block[k * Lanes + Lanes - 1];
                }

                for (var lane = 0; lane < Lanes; lane++)
                {
                    output[n + lane] = block[lane] + (block[Lanes + lane] + block[2 * Lanes + lane]);
                }
            }

            for (; n < length; n++)
            {
                StepOne(input, output, n, prev, prev2);
            }
        }

        private void StepOne(ReadOnlySpan<float> input, Span<float> output, int n, Span<float> prev, Span<float> prev2)
        {
            var left = n - _radius - 1;
            var right = n + _radius - 1;
            var sum = (left >= 0 ? input[left] : 0f) + (right < input.Length ? input[right] : 0f);
            Span<float> values = stackalloc float[3];
            for (var k = 0; k < 3; k++)
            {
                var value = sum * _mulIn[4 * k];
                value = MathF.FusedMultiplyAdd(_mulPrev2[4 * k], prev2[k], value);
                prev2[k] = prev[k];
                value = MathF.FusedMultiplyAdd(_mulPrev[4 * k], prev[k], value);
                prev[k] = value;
                values[k] = value;
            }

            if (n >= 0)
            {
                output[n] = values[0] + (values[1] + values[2]);
            }
        }

        /// <summary>The vertical pass of libjxl (<c>VerticalBlock</c>): every column at once, with fused multiply-adds.</summary>
        private void BlurColumns(ReadOnlySpan<float> input, Span<float> output, Span<float> state, int width, int height)
        {
            // Rows 0-2 of the state are the previous outputs of the three filters, rows 3-5 the outputs before them
            state.Clear();
            for (var n = -_radius + 1; n < height; n++)
            {
                var top = n - _radius - 1;
                var bottom = n + _radius - 1;
                for (var x = 0; x < width; x++)
                {
                    var sum = top >= 0
                        ? input[top * width + x] + (bottom < height ? input[bottom * width + x] : 0f)
                        : bottom < height ? input[bottom * width + x] : 0f;
                    for (var k = 0; k < 3; k++)
                    {
                        ref var previous = ref state[k * width + x];
                        ref var beforePrevious = ref state[(k + 3) * width + x];
                        var value = MathF.FusedMultiplyAdd(_n2[k], sum, MathF.FusedMultiplyAdd(-_d1[k], previous, -beforePrevious));
                        beforePrevious = previous;
                        previous = value;
                    }

                    if (n >= 0)
                    {
                        output[n * width + x] = state[x] + (state[width + x] + state[2 * width + x]);
                    }
                }
            }
        }

        private static double[] Invert3x3(double[] m)
        {
            var a0 = m[4] * m[8] - m[5] * m[7];
            var a1 = m[2] * m[7] - m[1] * m[8];
            var a2 = m[1] * m[5] - m[2] * m[4];
            var determinant = m[0] * a0 + m[3] * a1 + m[6] * a2;
            var inverseDeterminant = 1.0 / determinant;
            return
            [
                a0 * inverseDeterminant, a1 * inverseDeterminant, a2 * inverseDeterminant,
                (m[5] * m[6] - m[3] * m[8]) * inverseDeterminant, (m[0] * m[8] - m[2] * m[6]) * inverseDeterminant, (m[2] * m[3] - m[0] * m[5]) * inverseDeterminant,
                (m[3] * m[7] - m[4] * m[6]) * inverseDeterminant, (m[1] * m[6] - m[0] * m[7]) * inverseDeterminant, (m[0] * m[4] - m[1] * m[3]) * inverseDeterminant,
            ];
        }
    }
}
