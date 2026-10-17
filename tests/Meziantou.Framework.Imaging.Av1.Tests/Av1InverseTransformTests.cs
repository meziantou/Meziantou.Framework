using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// Sanity checks of the AV1 inverse transforms (AV1 specification, section 7.13) that do not depend on the implementation:
/// values worked out by hand from the steps of the specification, the mathematical definition of the transforms where the
/// integer arithmetic is exact, and structural properties. The bit-exact behavior on real streams is covered by
/// <c>Conformance/Av1DecoderConformanceTests</c>.
/// </summary>
public sealed class Av1InverseTransformTests
{
    // The intermediate clamping range of the one-dimensional transforms for 8-bit rows (8 + BitDepth bits)
    private const int ClampBits = 16;

    [Fact]
    public void WalshHadamardFollowsTheStepsOfTheSpecification()
    {
        // Section 7.13.2.10, shift 0, T = [1, 2, 3, 4]: a = 1, c = 2, d = 3, b = 4
        // a += c -> 3; d -= b -> -1; e = (a - d) >> 1 = 2; b = e - b = -2; c = e - c = 0; a -= b -> 5; d += c -> -1
        int[] values = [1, 2, 3, 4];
        Av1InverseTransform.InverseWalshHadamard(values, shift: 0);
        Assert.Equal(new[] { 5, -2, 0, -1 }, values);

        // T = [4, 0, 0, 0]: a = 4, e = 2, b = c = 2, a = 2, d = 2
        values = [4, 0, 0, 0];
        Av1InverseTransform.InverseWalshHadamard(values, shift: 0);
        Assert.Equal(new[] { 2, 2, 2, 2 }, values);

        // The inputs are shifted first: [16, 0, 0, 0] >> 2 is the previous case, and -5 >> 2 is -2 (arithmetic shift)
        values = [16, 0, 0, 0];
        Av1InverseTransform.InverseWalshHadamard(values, shift: 2);
        Assert.Equal(new[] { 2, 2, 2, 2 }, values);

        // [-5, 0, 0, 0] >> 2 = [-2, 0, 0, 0]: a = -2, e = -1, b = c = -1, a = -1, d = -1
        values = [-5, 0, 0, 0];
        Av1InverseTransform.InverseWalshHadamard(values, shift: 2);
        Assert.Equal(new[] { -1, -1, -1, -1 }, values);
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void WalshHadamardInvertsTheForwardTransform()
    {
        // The inverse is a sequence of lifting steps, each of which is undone exactly by the opposite step (see
        // ForwardWalshHadamard): whatever the samples, the inverse of the forward transform returns them unchanged
        var random = new Random(20240607);
        int[][] extremes = [[0, 0, 0, 0], [255, 255, 255, 255], [-255, 255, -255, 255], [4095, -4095, 4095, -4095], [1, 0, 0, 0], [0, 0, 0, -1], [-32768, 32767, -32768, 32767]];
        for (var i = 0; i < 2000; i++)
        {
            int[] samples = i < extremes.Length ? extremes[i] : [random.Next(-4096, 4096), random.Next(-4096, 4096), random.Next(-4096, 4096), random.Next(-4096, 4096)];
            var values = (int[])samples.Clone();
            ForwardWalshHadamard(values);
            Av1InverseTransform.InverseWalshHadamard(values, shift: 0);
            Assert.Equal(samples, values);
        }
    }

    [Theory]
    [InlineData(8, 255)]
    [InlineData(10, 1023)]
    [InlineData(12, 4095)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void LosslessBlocksInvertTheForwardTransform(int bitDepth, int maxResidual)
    {
        // A lossless 4x4 block (section 7.13.3): the rows are transformed with a shift of 2, then the columns without shift.
        // The forward transform undoes that in the opposite order: columns, then rows, then a multiplication by 4
        var random = new Random(bitDepth);
        Span<int> line = stackalloc int[4];
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var residual = new int[16];
            for (var i = 0; i < residual.Length; i++)
            {
                residual[i] = iteration switch
                {
                    0 => maxResidual,
                    1 => -maxResidual,
                    2 => ((i + (i / 4)) % 2) is 0 ? maxResidual : -maxResidual,
                    _ => random.Next(-maxResidual, maxResidual + 1),
                };
            }

            var coefficients = (int[])residual.Clone();
            for (var x = 0; x < 4; x++)
            {
                for (var y = 0; y < 4; y++)
                {
                    line[y] = coefficients[(y * 4) + x];
                }

                ForwardWalshHadamard(line);
                for (var y = 0; y < 4; y++)
                {
                    coefficients[(y * 4) + x] = line[y];
                }
            }

            for (var y = 0; y < 4; y++)
            {
                var row = coefficients.AsSpan(y * 4, 4);
                ForwardWalshHadamard(row);
                for (var x = 0; x < 4; x++)
                {
                    row[x] *= 4;
                }
            }

            var actual = new int[16];
            Av1InverseTransform.Transform(coefficients, actual, Av1Constants.Tx4x4, Av1Constants.DctDct, lossless: true, bitDepth);
            Assert.Equal(residual, actual);
        }
    }

    [Theory]
    [InlineData(4096, 2896)] // 4096 / sqrt(2) = 2896.3
    [InlineData(-4096, -2896)]
    [InlineData(1000, 707)] // 1000 * 2896 / 4096 = 707.03
    [InlineData(-1000, -707)]
    [InlineData(1, 1)] // 2896 / 4096 = 0.707 rounds to 1
    [InlineData(-1, -1)] // -0.707 rounds to -1
    [InlineData(3, 2)] // 2.12
    [InlineData(0, 0)]
    public void DcOnlyInverseDctIsFlat(int dc, int expected)
    {
        // The first basis function of the DCT is constant. The coefficient goes through one rotation by pi/4,
        // Round2(dc * cos128(32), 12) with cos128(32) = round(4096 * cos(pi/4)) = 2896, then only through additions of
        // zeros: every output sample has that value, whatever the size of the transform
        for (var n = 2; n <= 6; n++)
        {
            var values = new int[64];
            values[0] = dc;
            Av1InverseTransform.InverseDct(values, n, ClampBits);
            Assert.All(values[..(1 << n)], value => Assert.Equal(expected, value));
            Assert.All(values[(1 << n)..], value => Assert.Equal(0, value));
        }
    }

    [Theory]
    [InlineData(Av1Constants.Tx4x4, 4, 32)]
    [InlineData(Av1Constants.Tx8x8, 8, 16)]
    [InlineData(Av1Constants.Tx16x16, 16, 8)]
    public void DcOnlyBlocksAreFlat(int txSize, int size, int expected)
    {
        // DC coefficient 1024, 8-bit, by hand from section 7.13.3:
        // - row 0: Round2(1024 * 2896, 12) = 724 in every column, then Round2(724, rowShift) with rowShift 0, 1 and 2 for
        //   4x4, 8x8 and 16x16: 724, 362 and 181; the other rows are zero;
        // - columns: Round2(724 * 2896, 12) = 512, Round2(362 * 2896, 12) = 256, Round2(181 * 2896, 12) = 128;
        // - final Round2(x, 4): 32, 16 and 8
        var coefficients = new int[size * size];
        coefficients[0] = 1024;
        var residual = new int[size * size];
        Array.Fill(residual, int.MinValue);
        Av1InverseTransform.Transform(coefficients, residual, txSize, Av1Constants.DctDct, lossless: false, bitDepth: 8);
        Assert.All(residual, value => Assert.Equal(expected, value));
    }

    [Theory]
    [InlineData(Av1Constants.Tx32x32, 32)]
    [InlineData(Av1Constants.Tx64x64, 64)]
    public void DcOnlyLargeBlocksAreFlat(int txSize, int size)
    {
        // Only the 32x32 low-frequency coefficients of a 64-point dimension are coded
        var coded = Math.Min(size, 32);
        var coefficients = new int[coded * coded];
        coefficients[0] = 4096;
        var residual = new int[size * size];
        Array.Fill(residual, int.MinValue);
        Av1InverseTransform.Transform(coefficients, residual, txSize, Av1Constants.DctDct, lossless: false, bitDepth: 8);
        Assert.True(residual[0] > 0, "A positive DC coefficient gives a positive block.");
        Assert.All(residual, value => Assert.Equal(residual[0], value));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TransformFollowsTheTwoDimensionalProcessOnSparseBlocks()
    {
        // Transform skips the rows of zero coefficients and transforms a single column when only the first row is not zero and
        // its samples are equal: it must give the result of the process of section 7.13.3 applied to every row and column
        var random = new Random(71302);
        for (var txSz = 0; txSz < Av1Constants.TxSizesAll; txSz++)
        {
            var log2W = Av1Tables.TxWidthLog2[txSz];
            var log2H = Av1Tables.TxHeightLog2[txSz];
            var tw = Math.Min(32, 1 << log2W);
            var th = Math.Min(32, 1 << log2H);
            for (var txType = 0; txType < Av1Constants.TxTypes; txType++)
            {
                if (!IsSupported(Av1InverseTransform.GetRowKind(txType), log2W) || !IsSupported(Av1InverseTransform.GetColumnKind(txType), log2H))
                    continue;

                foreach (var lossless in txSz is Av1Constants.Tx4x4 ? new[] { false, true } : [false])
                {
                    foreach (var bitDepth in new[] { 8, 12 })
                    {
                        for (var pattern = 0; pattern < 8; pattern++)
                        {
                            var dequant = new int[tw * th];
                            var constant = random.Next(-3000, 3001);
                            for (var i = 0; i < dequant.Length; i++)
                            {
                                var row = i / tw;
                                var column = i % tw;
                                dequant[i] = pattern switch
                                {
                                    0 => 0,
                                    1 => i is 0 ? constant : 0, // DC only
                                    2 => row is 0 ? constant : 0, // a constant first row
                                    3 => row is 0 ? random.Next(-3000, 3001) : 0, // the first row only
                                    4 => column is 0 ? random.Next(-3000, 3001) : 0, // the first column only
                                    5 => row % 3 is 1 ? random.Next(-3000, 3001) : 0, // rows of zeros between rows of coefficients
                                    6 => row == th - 1 && column is 0 ? constant : 0, // only the last row
                                    _ => random.Next(4) is 0 ? random.Next(-3000, 3001) : 0,
                                };
                            }

                            var expected = TransformEveryRowAndColumn(dequant, txSz, txType, lossless, bitDepth);
                            var actual = new int[expected.Length];
                            Array.Fill(actual, int.MinValue);
                            Av1InverseTransform.Transform(dequant, actual, txSz, txType, lossless, bitDepth);
                            Assert.True(expected.AsSpan().SequenceEqual(actual), $"Transform size {txSz}, type {txType}, lossless {lossless}, {bitDepth}-bit, pattern {pattern}.");
                        }
                    }
                }
            }
        }

        // The ADST has 4 to 16 points and the identity 4 to 32 (section 7.13.3)
        static bool IsSupported(Av1InverseTransform.Kind kind, int n) => kind switch
        {
            Av1InverseTransform.Kind.Adst => n <= 4,
            Av1InverseTransform.Kind.Identity => n <= 5,
            _ => true,
        };
    }

    [Fact]
    public void FourPointDctMatchesItsDefinition()
    {
        // x[n] = sum over k of c(k) * T[k] * cos(pi * (2n + 1) * k / 8), with c(0) = 1 / sqrt(2) and c(k) = 1 otherwise.
        // For T = 4096 * e(k) the integer transform is exact: the coefficient is multiplied once by a 12-bit constant,
        // round(4096 * cos(...)), and Round2(4096 * constant, 12) is that constant; only additions of zeros follow
        for (var k = 0; k < 4; k++)
        {
            int[] values = [0, 0, 0, 0];
            values[k] = 4096;
            Av1InverseTransform.InverseDct(values, 2, ClampBits);
            var expected = Enumerable.Range(0, 4).Select(n => (int)Math.Round(4096 * (k is 0 ? Math.Sqrt(0.5) : 1) * Math.Cos(Math.PI * ((2 * n) + 1) * k / 8), MidpointRounding.AwayFromZero)).ToArray();
            Assert.Equal(expected, values);
        }

        // The same values, written out: cos128(32) = 2896, cos128(16) = 3784, cos128(48) = 1567
        int[] second = [0, 4096, 0, 0];
        Av1InverseTransform.InverseDct(second, 2, ClampBits);
        Assert.Equal(new[] { 3784, 1567, -1567, -3784 }, second);
    }

    [Fact]
    public void FourPointAdstMatchesItsDefinition()
    {
        // The 4-point ADST is the DST-VII: x[n] = (2 * sqrt(2) / 3) * sum over k of T[k] * sin(pi * (n + 1) * (2k + 1) / 9).
        // Its four 12-bit constants are round(4096 * (2 * sqrt(2) / 3) * sin(j * pi / 9)) for j = 1..4: 1321, 2482, 3344 and
        // 3803 (1321 + 2482 = 3803, as sin(20) + sin(40) = sin(80)). For T = 4096 * e(k) the integer transform is exact:
        // every product is a multiple of 4096
        for (var k = 0; k < 4; k++)
        {
            int[] values = [0, 0, 0, 0];
            values[k] = 4096;
            Av1InverseTransform.InverseAdst(values, 2, ClampBits);
            var expected = Enumerable.Range(0, 4).Select(n => (int)Math.Round(4096 * (2 * Math.Sqrt(2) / 3) * Math.Sin(Math.PI * (n + 1) * ((2 * k) + 1) / 9), MidpointRounding.AwayFromZero)).ToArray();
            Assert.Equal(expected, values);
        }

        int[] first = [4096, 0, 0, 0];
        Av1InverseTransform.InverseAdst(first, 2, ClampBits);
        Assert.Equal(new[] { 1321, 2482, 3344, 3803 }, first);

        int[] third = [0, 0, -4096, 0];
        Av1InverseTransform.InverseAdst(third, 2, ClampBits);
        Assert.Equal(new[] { -3803, 1321, 3344, -2482 }, third);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void DctBasisFunctionsAreSymmetricOrAntisymmetric(int n)
    {
        // cos(pi * (2 * (N - 1 - i) + 1) * k / (2N)) = (-1)^k * cos(pi * (2i + 1) * k / (2N)): an even basis function is
        // symmetric and an odd one antisymmetric. The integer transform keeps the property exactly: its last step outputs
        // even[i] + odd[i] and even[i] - odd[i], and the half that receives no coefficient stays zero
        var size = 1 << n;
        for (var k = 0; k < size; k++)
        {
            foreach (var amplitude in new[] { 4096, -4096 })
            {
                var values = new int[size];
                values[k] = amplitude;
                Av1InverseTransform.InverseDct(values, n, ClampBits);
                Assert.Contains(values, value => value != 0);
                for (var i = 0; i < size / 2; i++)
                {
                    var mirrored = values[size - 1 - i];
                    Assert.Equal((k % 2) is 0 ? mirrored : -mirrored, values[i], message: $"{size}-point DCT, coefficient {k}: samples {i} and {size - 1 - i}.");
                }

                // The first sample of every basis function has the sign of the coefficient: cos(pi * k / (2N)) > 0
                Assert.Equal(Math.Sign(amplitude), Math.Sign(values[0]));
            }
        }
    }

    /// <summary>
    /// The 2D inverse transform process of section 7.13.3, step by step: every row, then every column, with the one-dimensional
    /// transforms of <see cref="Av1InverseTransform"/>.
    /// </summary>
    private static int[] TransformEveryRowAndColumn(int[] dequant, int txSz, int txType, bool lossless, int bitDepth)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var rowShift = lossless ? 0 : Av1Tables.TransformRowShift[txSz];
        var colShift = lossless ? 0 : 4;
        var rowClampRange = bitDepth + 8;
        var colClampRange = Math.Max(bitDepth + 6, 16);
        var residual = new int[w * h];
        var t = new int[64];
        for (var i = 0; i < h; i++)
        {
            for (var j = 0; j < w; j++)
            {
                t[j] = i < th && j < tw ? dequant[(i * tw) + j] : 0;
                if (Math.Abs(log2W - log2H) is 1)
                    t[j] = Round2(t[j] * 2896L, 12);
            }

            Transform1D(t.AsSpan(0, w), Av1InverseTransform.GetRowKind(txType), log2W, rowClampRange, shift: 2);
            for (var j = 0; j < w; j++)
            {
                residual[(i * w) + j] = Math.Clamp(Round2(t[j], rowShift), -(1 << (colClampRange - 1)), (1 << (colClampRange - 1)) - 1);
            }
        }

        for (var j = 0; j < w; j++)
        {
            for (var i = 0; i < h; i++)
            {
                t[i] = residual[(i * w) + j];
            }

            Transform1D(t.AsSpan(0, h), Av1InverseTransform.GetColumnKind(txType), log2H, colClampRange, shift: 0);
            for (var i = 0; i < h; i++)
            {
                residual[(i * w) + j] = Round2(t[i], colShift);
            }
        }

        return residual;

        void Transform1D(Span<int> values, Av1InverseTransform.Kind kind, int n, int r, int shift)
        {
            if (lossless)
            {
                Av1InverseTransform.InverseWalshHadamard(values, shift);
            }
            else if (kind is Av1InverseTransform.Kind.Dct)
            {
                Av1InverseTransform.InverseDct(values, n, r);
            }
            else if (kind is Av1InverseTransform.Kind.Adst)
            {
                Av1InverseTransform.InverseAdst(values, n, r);
            }
            else
            {
                Av1InverseTransform.InverseIdentity(values, n);
            }
        }

        static int Round2(long value, int bits) => bits is 0 ? (int)value : (int)((value + (1L << (bits - 1))) >> bits);
    }

    /// <summary>
    /// The forward 4-point Walsh-Hadamard transform, obtained by undoing the steps of the inverse transform of the
    /// specification (section 7.13.2.10) in the opposite order. The inverse reads a, c, d, b from T[0..3], then:
    /// a += c; d -= b; e = (a - d) >> 1; b = e - b; c = e - c; a -= b; d += c, and writes a, b, c, d.
    /// </summary>
    private static void ForwardWalshHadamard(Span<int> values)
    {
        var a = values[0];
        var b = values[1];
        var c = values[2];
        var d = values[3];
        d -= c; // undoes d += c
        a += b; // undoes a -= b
        var e = (a - d) >> 1; // a and d have the values the inverse computed e from
        c = e - c; // undoes c = e - c
        b = e - b; // undoes b = e - b
        d += b; // undoes d -= b
        a -= c; // undoes a += c
        values[0] = a;
        values[1] = c;
        values[2] = d;
        values[3] = b;
    }
}
