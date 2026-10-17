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
                // reconstructed by the inverse transform within the rounding of both
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
                    for (var i = 0; i < w * h; i++)
                    {
                        Assert.True(Math.Abs(residual[i] - inverse[i]) <= 2, $"transform size {txSz}, type {txType}, sample {i}: {residual[i]} became {inverse[i]}");
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
