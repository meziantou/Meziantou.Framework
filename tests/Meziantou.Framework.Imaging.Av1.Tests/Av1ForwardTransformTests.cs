using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The forward transforms of the encoder against the inverse transforms of the specification (section 7.13), which the
/// decoder implements and its own tests check against hand-computed values: a forward transform is correct when the inverse
/// transform undoes it.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class Av1ForwardTransformTests
{
    [Fact]
    public void TheWalshHadamardTransformIsExactlyInvertedByTheLosslessInverseTransform()
    {
        var random = new Random(1);
        Span<int> residual = stackalloc int[16];
        Span<int> coefficients = stackalloc int[16];
        Span<int> dequantized = stackalloc int[16];
        Span<int> inverse = stackalloc int[16];
        for (var iteration = 0; iteration < 2000; iteration++)
        {
            // Residuals of 12-bit samples, the extreme ones included
            for (var i = 0; i < 16; i++)
            {
                residual[i] = iteration switch
                {
                    0 => 4095,
                    1 => -4095,
                    2 => (i & 1) is 0 ? 4095 : -4095,
                    _ => random.Next(-4095, 4096),
                };
            }

            Av1ForwardTransform.ForwardWalshHadamard4x4(residual, coefficients);

            // The quantizers of the lossless coding are 4, and the dequantized coefficients have at most 24 bits
            for (var i = 0; i < 16; i++)
            {
                dequantized[i] = coefficients[i] * 4;
                Assert.True(Math.Abs(dequantized[i]) < 1 << 19);
            }

            Av1InverseTransform.Transform(dequantized, inverse, Av1Constants.Tx4x4, Av1Constants.DctDct, lossless: true, bitDepth: 12);
            Assert.Equal(residual.ToArray(), inverse.ToArray());
        }
    }

    [Fact]
    public void EveryTransformSizeAndTypeIsUndoneByTheInverseTransform()
    {
        var random = new Random(2);
        var coefficients = new int[32 * 32];
        var residual = new int[64 * 64];
        var transformed = new int[64 * 64];
        var inverse = new int[64 * 64];
        var forward = new int[32 * 32];
        var worst = 0;
        for (var txSz = 0; txSz < Av1Constants.TxSizesAll; txSz++)
        {
            int w = Av1Tables.TxWidth[txSz];
            int h = Av1Tables.TxHeight[txSz];
            var tw = Math.Min(32, w);
            var th = Math.Min(32, h);
            for (var txType = 0; txType < Av1Constants.TxTypes; txType++)
            {
                // The transforms of 64 points are DCTs; the ones of 32 points are DCTs or identities
                var largest = Math.Max(w, h);
                if (largest is 64 && txType is not Av1Constants.DctDct)
                    continue;

                if (largest is 32 && txType is not (Av1Constants.DctDct or Av1Constants.Idtx))
                    continue;

                var flipUD = txType is Av1Constants.FlipadstDct or Av1Constants.FlipadstAdst or Av1Constants.VFlipadst or Av1Constants.FlipadstFlipadst;
                var flipLR = txType is Av1Constants.DctFlipadst or Av1Constants.AdstFlipadst or Av1Constants.HFlipadst or Av1Constants.FlipadstFlipadst;
                for (var iteration = 0; iteration < 6; iteration++)
                {
                    // A few large coefficients, as a decoder dequantizes them: the residual is far larger than its rounding
                    Array.Clear(coefficients);
                    for (var i = 0; i <= iteration; i++)
                    {
                        coefficients[random.Next(tw * th)] = random.Next(-30000, 30001);
                    }

                    Av1InverseTransform.Transform(coefficients.AsSpan(0, tw * th), transformed.AsSpan(0, w * h), txSz, txType, lossless: false, bitDepth: 12);
                    Mirror(transformed, residual);
                    Av1ForwardTransform.Forward(residual.AsSpan(0, w * h), forward.AsSpan(0, tw * th), txSz, txType);

                    // The inverse transform rounds the residual to integers: an error of variance 1/12 per sample, which an
                    // orthonormal transform keeps and the 3 fractional bits of the coefficients multiply by 8 (2.3 units).
                    // The rotations of the specification have 12 fractional bits: its kernels are orthogonal to about one
                    // part in a thousand, which is what a coefficient leaks into the others
                    var tolerance = 24 + (coefficients.Max(Math.Abs) >> 9);
                    for (var i = 0; i < tw * th; i++)
                    {
                        var error = Math.Abs(coefficients[i] - forward[i]);
                        worst = Math.Max(worst, error);
                        Assert.True(error <= tolerance, $"transform size {txSz}, type {txType}, coefficient {i}: {coefficients[i]} became {forward[i]}");
                    }
                }

                // The other direction, for the sizes that keep every coefficient: a residual is transformed, then
                // reconstructed by the inverse transform within the rounding of both, and within the precision of the
                // division by 2^n / 2 (the squared norms of the bases differ from it by up to 7 parts in 10,000)
                if (largest is 64)
                    continue;

                for (var iteration = 0; iteration < 6; iteration++)
                {
                    var amplitude = iteration < 3 ? 255 : 4095;
                    for (var i = 0; i < w * h; i++)
                    {
                        residual[i] = iteration is 0 ? amplitude : random.Next(-amplitude, amplitude + 1);
                    }

                    Av1ForwardTransform.Forward(residual.AsSpan(0, w * h), forward.AsSpan(0, tw * th), txSz, txType);
                    Av1InverseTransform.Transform(forward.AsSpan(0, tw * th), transformed.AsSpan(0, w * h), txSz, txType, lossless: false, bitDepth: 12);
                    Mirror(transformed, inverse);
                    var tolerance = 2 + (amplitude >> 10);
                    for (var i = 0; i < w * h; i++)
                    {
                        Assert.True(Math.Abs(residual[i] - inverse[i]) <= tolerance, $"transform size {txSz}, type {txType}, sample {i}: {residual[i]} became {inverse[i]}");
                    }
                }

                // The reconstruction process (section 7.12.3) mirrors the residual of the FLIPADST types when it adds it to
                // the prediction: the residual of the block is the mirrored output of the inverse transform
                void Mirror(int[] source, int[] destination)
                {
                    for (var i = 0; i < h; i++)
                    {
                        for (var j = 0; j < w; j++)
                        {
                            destination[((flipUD ? h - i - 1 : i) * w) + (flipLR ? w - j - 1 : j)] = source[(i * w) + j];
                        }
                    }
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"largest error: {worst}");
    }

    [Fact]
    public void TheCosinesExtendTheTableOfTheSpecification()
    {
        // cos128( angle ) of section 7.13.2.1, from Cos128_Lookup and the symmetries of the cosine
        var lookup = Av1Tables.Cos128Lookup;
        for (var angle = 0; angle < 256; angle++)
        {
            var expected = angle switch
            {
                <= 64 => lookup[angle],
                <= 128 => -lookup[128 - angle],
                <= 192 => -lookup[angle - 128],
                _ => lookup[256 - angle],
            };
            Assert.Equal(expected, Av1ForwardTransform.Cos128Values[angle]);
        }
    }

    [Fact]
    public void TheBasesOfTheInverseKernelsHaveTheSquaredNormOfTheirLength()
    {
        // The forward transform divides the transposed kernels of 2^n points by 2^n / 2, the squared norm of their bases
        for (var n = 2; n <= 6; n++)
        {
            foreach (var kind in new[] { Av1InverseTransform.Kind.Dct, Av1InverseTransform.Kind.Adst, Av1InverseTransform.Kind.Identity })
            {
                if (!IsApplicable(kind, n))
                    continue;

                var bases = GetBases(kind, n);
                for (var k = 0; k < bases.Length; k++)
                {
                    var norm = bases[k].Sum(value => value * value) / ((1 << n) / 2.0);
                    Assert.True(Math.Abs(norm - 1) < 1.0 / 1024, $"{kind} of {1 << n} points, basis {k}: {norm}");
                }
            }
        }
    }

    [Fact]
    public void EveryTransformIsTheProductOfTheBasesOfTheInverseKernels()
    {
        // The exact product of the residual by the bases of the decoder (the responses of its inverse kernels), divided by
        // their squared norms 2^n / 2, then multiplied by the scale of the inverse process: 2^(4 + Transform_Row_Shift), and
        // sqrt(2) for a 2:1 rectangle. The forward transform only differs from it by the roundings of its rotations: at most
        // 3 units, for coefficients of up to 2^19
        var random = new Random(3);
        var residual = new int[64 * 64];
        var forward = new int[32 * 32];
        var worst = 0.0;
        for (var txSz = 0; txSz < Av1Constants.TxSizesAll; txSz++)
        {
            int log2W = Av1Tables.TxWidthLog2[txSz];
            int log2H = Av1Tables.TxHeightLog2[txSz];
            var w = 1 << log2W;
            var h = 1 << log2H;
            var tw = Math.Min(32, w);
            var th = Math.Min(32, h);
            var scale = Math.Pow(2, Av1Tables.TransformRowShift[txSz] + 4) * (Math.Abs(log2W - log2H) is 1 ? 5793 / 4096.0 : 1);
            for (var txType = 0; txType < Av1Constants.TxTypes; txType++)
            {
                var rowKind = Av1InverseTransform.GetRowKind(txType);
                var colKind = Av1InverseTransform.GetColumnKind(txType);
                if (!IsApplicable(rowKind, log2W) || !IsApplicable(colKind, log2H))
                    continue;

                var flipUD = txType is Av1Constants.FlipadstDct or Av1Constants.FlipadstAdst or Av1Constants.VFlipadst or Av1Constants.FlipadstFlipadst;
                var flipLR = txType is Av1Constants.DctFlipadst or Av1Constants.AdstFlipadst or Av1Constants.HFlipadst or Av1Constants.FlipadstFlipadst;
                var rowBases = GetBases(rowKind, log2W);
                var colBases = GetBases(colKind, log2H);
                for (var round = 0; round < 10; round++)
                {
                    // Random, extreme (the largest differences of 12-bit samples), sparse and smooth residuals, and the
                    // residual of 12-bit samples that gives a coefficient its largest magnitude: the signs of its basis
                    var (worstK, worstL) = (random.Next(th), random.Next(tw));
                    for (var i = 0; i < w * h; i++)
                    {
                        var (r, c) = (i / w, i % w);
                        residual[i] = (round % 5) switch
                        {
                            0 => random.Next(-4095, 4096),
                            1 => random.Next(2) is 0 ? -4095 : 4095,
                            2 => random.Next(8) is 0 ? random.Next(-300, 301) : 0,
                            3 => (int)(2000 * Math.Sin(c * 0.3) * Math.Cos(r * 0.2)),
                            _ => colBases[worstK][flipUD ? h - 1 - r : r] * rowBases[worstL][flipLR ? w - 1 - c : c] < 0 ? -4095 : 4095,
                        };
                    }

                    Av1ForwardTransform.Forward(residual.AsSpan(0, w * h), forward.AsSpan(0, tw * th), txSz, txType);

                    // The columns, then the rows; the FLIPADST types read the samples from the other end
                    var columns = new double[th * w];
                    for (var k = 0; k < th; k++)
                    {
                        for (var c = 0; c < w; c++)
                        {
                            var sum = 0.0;
                            for (var r = 0; r < h; r++)
                            {
                                sum += colBases[k][r] * residual[((flipUD ? h - 1 - r : r) * w) + c];
                            }

                            columns[(k * w) + c] = sum / (h / 2);
                        }
                    }

                    for (var k = 0; k < th; k++)
                    {
                        for (var l = 0; l < tw; l++)
                        {
                            var sum = 0.0;
                            for (var c = 0; c < w; c++)
                            {
                                sum += rowBases[l][c] * columns[(k * w) + (flipLR ? w - 1 - c : c)];
                            }

                            var exact = sum / (w / 2) * scale;
                            var error = Math.Abs(forward[(k * tw) + l] - exact);
                            worst = Math.Max(worst, error);
                            Assert.True(error <= 3, $"transform size {txSz}, type {txType}, round {round}, coefficient {(k * tw) + l}: {forward[(k * tw) + l]} instead of {exact}");
                        }
                    }
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"largest error: {worst}");
    }

    // The response of the inverse kernel of 2^n points to each of its first 32 coefficients: a large impulse, so that the
    // roundings of the kernel are negligible
    private static double[][] GetBases(Av1InverseTransform.Kind kind, int n)
    {
        const int Impulse = 1 << 22;
        var length = 1 << n;
        var bases = new double[Math.Min(32, length)][];
        var t = new int[length];
        for (var k = 0; k < bases.Length; k++)
        {
            Array.Clear(t);
            t[k] = Impulse;
            switch (kind)
            {
                case Av1InverseTransform.Kind.Dct:
                    Av1InverseTransform.InverseDct(t, n, 31);
                    break;
                case Av1InverseTransform.Kind.Adst:
                    Av1InverseTransform.InverseAdst(t, n, 31);
                    break;
                default:
                    Av1InverseTransform.InverseIdentity(t, n);
                    break;
            }

            bases[k] = t.Select(value => value / (double)Impulse).ToArray();
        }

        return bases;
    }

    private static bool IsApplicable(Av1InverseTransform.Kind kind, int log2Size) => kind switch
    {
        Av1InverseTransform.Kind.Adst => log2Size <= 4,
        Av1InverseTransform.Kind.Identity => log2Size <= 5,
        _ => true,
    };

    [Fact]
    public void AConstantResidualIsADcCoefficient()
    {
        // A 4x4 block of residual v: the inverse DCT of a DC coefficient c alone is c / 32 in every sample (two passes that
        // each scale by cos(pi / 4), then the final shift by 4)
        Span<int> residual = stackalloc int[16];
        Span<int> coefficients = stackalloc int[16];
        residual.Fill(25);
        Av1ForwardTransform.Forward(residual, coefficients, Av1Constants.Tx4x4, Av1Constants.DctDct);
        Assert.Equal(800, coefficients[0]);
        for (var i = 1; i < 16; i++)
        {
            Assert.Equal(0, coefficients[i]);
        }
    }
}
