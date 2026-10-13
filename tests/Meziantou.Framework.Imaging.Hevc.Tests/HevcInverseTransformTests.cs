using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The transformation process for scaled transform coefficients (Rec. ITU-T H.265, clause 8.6.4): literal values worked out
/// by hand from the matrices of the specification, and the two-stage definition applied to one coefficient at a time.
/// </summary>
public sealed class HevcInverseTransformTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ADcCoefficientGivesAConstantBlock(int log2TrafoSize)
    {
        // The first basis function is 64: the column is 64 * 100, g = ( 6400 + 64 ) >> 7 = 50, and each row is 64 * 50
        var nTbS = 1 << log2TrafoSize;
        var block = new int[nTbS * nTbS];
        block[0] = 100;
        HevcInverseTransform.Transform(block, log2TrafoSize, trType: 0, new int[nTbS * nTbS]);
        Assert.All(block, value => Assert.Equal(3200, value));
    }

    [Fact]
    public void TheSineTransformOfA4x4BlockIsTheOneOfItsMatrix()
    {
        // The coefficient ( 0, 0 ) alone, of value 128: the column 0 is { 29, 55, 74, 84 } * 128, g = ( e + 64 ) >> 7 =
        // { 29, 55, 74, 84 }, and the row y is g[ y ] * { 29, 55, 74, 84 }
        var block = new int[16];
        block[0] = 128;
        HevcInverseTransform.Transform(block, 2, trType: 1, new int[16]);
        int[] basis = [29, 55, 74, 84];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(basis[y] * basis[x], block[(y * 4) + x]);
            }
        }

        // The coefficient ( 2, 1 ) alone, of value -64: the column 2 is { 74, 74, 0, -74 } * -64, g = { -37, -37, 0, 37 },
        // and the row y is g[ y ] * { 84, -29, -74, 55 }
        Array.Clear(block);
        block[(1 * 4) + 2] = -64;
        HevcInverseTransform.Transform(block, 2, trType: 1, new int[16]);
        int[] g = [-37, -37, 0, 37];
        int[] row = [84, -29, -74, 55];
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(g[y] * row[x], block[(y * 4) + x]);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ACoefficientAloneGivesItsBasisFunctions()
    {
        // The coefficient ( u, v ) of value c: the column u is the basis function v times c, rounded and clipped to 16 bits,
        // and the row y is g[ y ] times the basis function u (Equations 8-314 and 8-317)
        var random = new Random(265);
        var matrix = HevcTransformTables.TransMatrix;
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var log2TrafoSize = random.Next(2, 6);
            var nTbS = 1 << log2TrafoSize;
            var step = 32 >> log2TrafoSize;
            var u = random.Next(nTbS);
            var v = random.Next(nTbS);
            var c = random.Next(-32768, 32768);
            var block = new int[nTbS * nTbS];
            block[(v * nTbS) + u] = c;
            HevcInverseTransform.Transform(block, log2TrafoSize, trType: 0, new int[nTbS * nTbS]);
            for (var y = 0; y < nTbS; y++)
            {
                var g = Math.Clamp(((matrix[(v * step * 32) + y] * c) + 64) >> 7, -32768, 32767);
                for (var x = 0; x < nTbS; x++)
                {
                    Assert.Equal(g * matrix[(u * step * 32) + x], block[(y * nTbS) + x]);
                }
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ABlockIsTheSumOfTheMatrixProducts()
    {
        // The definition, with every sum written out, on random blocks whose intermediate values are clipped or not
        var random = new Random(8640);
        for (var iteration = 0; iteration < 60; iteration++)
        {
            var log2TrafoSize = random.Next(2, 6);
            var trType = log2TrafoSize is 2 ? random.Next(2) : 0;
            var nTbS = 1 << log2TrafoSize;
            var magnitude = iteration % 3 is 0 ? 32768 : 300;
            var d = new int[nTbS * nTbS];
            for (var i = 0; i < d.Length; i++)
            {
                d[i] = random.Next(4) is 0 ? random.Next(-magnitude, magnitude) : 0;
            }

            var g = new int[nTbS * nTbS];
            var expected = new int[nTbS * nTbS];
            for (var x = 0; x < nTbS; x++)
            {
                for (var y = 0; y < nTbS; y++)
                {
                    long e = 0;
                    for (var j = 0; j < nTbS; j++)
                    {
                        e += (long)GetMatrix(trType, log2TrafoSize, j, y) * d[(j * nTbS) + x];
                    }

                    g[(y * nTbS) + x] = (int)Math.Clamp((e + 64) >> 7, -32768, 32767);
                }
            }

            for (var y = 0; y < nTbS; y++)
            {
                for (var x = 0; x < nTbS; x++)
                {
                    long r = 0;
                    for (var j = 0; j < nTbS; j++)
                    {
                        r += (long)GetMatrix(trType, log2TrafoSize, j, x) * g[(y * nTbS) + j];
                    }

                    expected[(y * nTbS) + x] = (int)r;
                }
            }

            HevcInverseTransform.Transform(d, log2TrafoSize, trType, new int[nTbS * nTbS]);
            Assert.Equal(expected, d);
        }

        // transMatrix[ sample ][ coefficient * 2^( 5 - Log2( nTbS ) ) ], or the matrix of the transform type 1
        static int GetMatrix(int trType, int log2TrafoSize, int coefficient, int sample)
        {
            return trType is 1 ? HevcTransformTables.DstMatrix[(coefficient * 4) + sample] : HevcTransformTables.TransMatrix[(coefficient * (32 >> log2TrafoSize) * 32) + sample];
        }
    }
}
