using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The constant tables of the HEVC decoder, which are transcribed from Rec. ITU-T H.265: the properties that the
/// specification gives them, which a transcription error breaks, and the values that the specification derives by a
/// process, worked out by hand.
/// </summary>
public sealed class HevcTableTests
{
    [Fact]
    public void TheTransformMatrixIsTheOneOfADiscreteCosineTransform()
    {
        var matrix = HevcTransformTables.TransMatrix;
        Assert.HasCount(32 * 32, matrix.ToArray());
        for (var n = 0; n < 32; n++)
        {
            // The first basis function is constant; the row n is symmetric when n is even and antisymmetric when it is odd
            Assert.Equal(64, matrix[n]);
            for (var m = 0; m < 32; m++)
            {
                Assert.Equal((n & 1) is 0 ? matrix[(n * 32) + m] : -matrix[(n * 32) + m], matrix[(n * 32) + 31 - m]);

                // The value is the one of the cosine, within the rounding of the specification: 64 * sqrt( 2 ) * cos( ... )
                var expected = n is 0 ? 64 : 64 * Math.Sqrt(2) * Math.Cos(Math.PI * n * ((2 * m) + 1) / 64);
                Assert.True(Math.Abs(matrix[(n * 32) + m] - expected) < 1.6, $"transMatrix[{m}][{n}] is {matrix[(n * 32) + m]}; the cosine is {expected}");
            }
        }

        // The rows are orthogonal within the precision of 8-bit coefficients, and have the norm 64 * sqrt( 32 )
        for (var a = 0; a < 32; a++)
        {
            for (var b = a; b < 32; b++)
            {
                var product = 0;
                for (var m = 0; m < 32; m++)
                {
                    product += matrix[(a * 32) + m] * matrix[(b * 32) + m];
                }

                if (a == b)
                {
                    Assert.True(Math.Abs(product - (64 * 64 * 32)) < 700, $"The norm of the row {a} is {product}");
                }
                else
                {
                    Assert.True(Math.Abs(product) < 700, $"The rows {a} and {b} are not orthogonal: {product}");
                }
            }
        }

        // Values of the specification that the transforms of every size share: the rows 16 and 8 of the first columns
        Assert.Equal(new sbyte[] { 64, -64, -64, 64 }, matrix.Slice(16 * 32, 4).ToArray());
        Assert.Equal(new sbyte[] { 83, 36, -36, -83 }, matrix.Slice(8 * 32, 4).ToArray());
        Assert.Equal(new sbyte[] { 90, 90, 88, 85, 82, 78, 73, 67 }, matrix.Slice(32, 8).ToArray());
        Assert.Equal(new sbyte[] { 29, 55, 74, 84, 74, 74, 0, -74, 84, -29, -74, 55, 55, -84, 74, -29 }, HevcTransformTables.DstMatrix.ToArray());
    }

    [Fact]
    public void TheInverseAngleIsTheRoundedInverseOfTheAngle()
    {
        var angles = HevcIntraTables.IntraPredAngle;
        Assert.HasCount(35, angles.ToArray());
        for (var mode = 2; mode <= 34; mode++)
        {
            // The angles are symmetric around the diagonal mode 18, and the modes 10 and 26 are the horizontal and the
            // vertical ones
            Assert.Equal(angles[mode], angles[36 - mode]);
            Assert.True(angles[mode] is >= -32 and <= 32);
        }

        Assert.Equal((0, 0, 32, -32, 32), (angles[10], angles[26], angles[2], angles[18], angles[34]));

        // Table 8-6: invAngle is Round( 8192 / intraPredAngle )
        for (var mode = 11; mode <= 25; mode++)
        {
            Assert.Equal((int)Math.Round(8192.0 / angles[mode], MidpointRounding.AwayFromZero), HevcIntraTables.InvAngle[mode - 11]);
        }

        // Table 8-3: the modes of 4:2:2 chroma never decrease, and keep the non-angular modes and the vertical mode
        var mode422 = HevcIntraTables.Mode422;
        Assert.HasCount(35, mode422.ToArray());
        Assert.Equal((0, 1, 26, 31), (mode422[0], mode422[1], mode422[26], mode422[34]));
        for (var mode = 1; mode < mode422.Length; mode++)
        {
            Assert.True(mode422[mode] >= mode422[mode - 1]);
        }
    }

    [Fact]
    public void ScanOrdersVisitEveryPositionOnce()
    {
        for (var log2BlockSize = 0; log2BlockSize <= 3; log2BlockSize++)
        {
            var blkSize = 1 << log2BlockSize;
            for (var scanIdx = 0; scanIdx < 3; scanIdx++)
            {
                var scan = HevcScanOrder.Get(log2BlockSize, scanIdx);
                Assert.HasCount(blkSize * blkSize, scan.ToArray());
                var seen = new bool[blkSize * blkSize];
                foreach (var position in scan)
                {
                    var x = HevcScanOrder.X(position);
                    var y = HevcScanOrder.Y(position);
                    Assert.True(x < blkSize && y < blkSize && !seen[(y * blkSize) + x]);
                    seen[(y * blkSize) + x] = true;
                }

                Assert.Equal(0, scan[0]);
            }
        }

        // Clause 6.5.3 by hand for a 4x4 block: each diagonal from its bottom-left position to its top-right one
        (int X, int Y)[] diagonal = [(0, 0), (0, 1), (1, 0), (0, 2), (1, 1), (2, 0), (0, 3), (1, 2), (2, 1), (3, 0), (1, 3), (2, 2), (3, 1), (2, 3), (3, 2), (3, 3)];
        Assert.Equal(diagonal, HevcScanOrder.Get(2, HevcScanOrder.Diagonal).ToArray().Select(position => (HevcScanOrder.X(position), HevcScanOrder.Y(position))).ToArray());

        // Clauses 6.5.4 and 6.5.5 for a 2x2 block: row by row, column by column
        Assert.Equal(new byte[] { 0x00, 0x01, 0x10, 0x11 }, HevcScanOrder.Get(1, HevcScanOrder.Horizontal).ToArray());
        Assert.Equal(new byte[] { 0x00, 0x10, 0x01, 0x11 }, HevcScanOrder.Get(1, HevcScanOrder.Vertical).ToArray());
    }

    [Fact]
    public void TheTablesOfTheArithmeticDecoderHaveTheirProperties()
    {
        // Table 9-52: the range of the least probable symbol decreases with its probability and increases with the range
        var rangeTabLps = HevcCabacTables.RangeTabLps;
        Assert.HasCount(64 * 4, rangeTabLps.ToArray());
        for (var pStateIdx = 0; pStateIdx < 64; pStateIdx++)
        {
            for (var qRangeIdx = 0; qRangeIdx < 4; qRangeIdx++)
            {
                var value = rangeTabLps[(pStateIdx * 4) + qRangeIdx];
                Assert.True(pStateIdx is 0 || value <= rangeTabLps[((pStateIdx - 1) * 4) + qRangeIdx]);
                Assert.True(qRangeIdx is 0 || pStateIdx is 63 || value > rangeTabLps[(pStateIdx * 4) + qRangeIdx - 1]);
            }
        }

        Assert.Equal(new byte[] { 128, 176, 208, 240 }, rangeTabLps[..4].ToArray());
        Assert.Equal(new byte[] { 6, 7, 8, 9, 2, 2, 2, 2 }, rangeTabLps[(62 * 4)..].ToArray());

        // Table 9-53: a most probable symbol moves to the next state up to the state 62; a least probable one moves back
        for (var pStateIdx = 0; pStateIdx < 64; pStateIdx++)
        {
            Assert.Equal(pStateIdx is 63 ? 63 : Math.Min(pStateIdx + 1, 62), HevcCabacTables.TransIdxMps[pStateIdx]);
            Assert.True(pStateIdx is 63 || HevcCabacTables.TransIdxLps[pStateIdx] <= Math.Max(pStateIdx - 1, 0));
        }

        Assert.Equal(63, HevcCabacTables.TransIdxLps[63]);

        // Table 9-4: 173 context variables, the same number for the three initialization types
        Assert.Equal(173, HevcContextLayout.Count);
        Assert.Equal(HevcContextLayout.Count, HevcContextLayout.ResScaleSignFlag + 2);
        Assert.Equal(44, HevcContextLayout.CoeffAbsLevelGreater1Flag - HevcContextLayout.SigCoeffFlag);
        Assert.HasCount(15, HevcCabacTables.CtxIdxMap.ToArray());
    }

    [Fact]
    public void TheTablesOfTheQuantizationHaveTheirProperties()
    {
        // Table 8-10 never decreases, and joins the identity below 30 and qPi - 6 above 43
        Assert.Equal((29, 29, 37, 38, -5), (HevcSliceCoder.MapChromaQp420(29), HevcSliceCoder.MapChromaQp420(30), HevcSliceCoder.MapChromaQp420(43), HevcSliceCoder.MapChromaQp420(44), HevcSliceCoder.MapChromaQp420(-5)));
        for (var qPi = -24; qPi < 57; qPi++)
        {
            var difference = HevcSliceCoder.MapChromaQp420(qPi + 1) - HevcSliceCoder.MapChromaQp420(qPi);
            Assert.True(difference is 0 or 1);
        }

        // Table 8-12 never decreases: beta from 6 at Q = 16 to 64 at Q = 51, tC from 1 at Q = 18 to 24 at Q = 53
        var beta = HevcQuantizationTables.BetaPrime;
        var tc = HevcQuantizationTables.TcPrime;
        Assert.Equal((52, 54), (beta.Length, tc.Length));
        Assert.Equal((0, 6, 64, 0, 1, 24), (beta[15], beta[16], beta[51], tc[17], tc[18], tc[53]));
        for (var q = 1; q < beta.Length; q++)
        {
            Assert.True(beta[q] >= beta[q - 1] && beta[q] - beta[q - 1] <= 6);
        }

        for (var q = 1; q < tc.Length; q++)
        {
            Assert.True(tc[q] >= tc[q - 1] && tc[q] - tc[q - 1] <= 2);
        }

        // levelScale doubles every 6 steps of the quantization parameter: its values are 64 * 2^( ( k - 4 ) / 6 ), rounded
        var levelScale = HevcQuantizationTables.LevelScale;
        for (var k = 0; k < 6; k++)
        {
            Assert.Equal((int)Math.Round(64 * Math.Pow(2, (k - 4) / 6.0), MidpointRounding.AwayFromZero), levelScale[k]);
        }
    }

    [Fact]
    public void TheDefaultScalingFactorsAreSymmetricAndFlatFor4x4Blocks()
    {
        var factors = new byte[HevcScalingList.ScalingFactorLength];
        HevcScalingList.Default.WriteScalingFactors(factors, chromaArrayType: 3);
        for (var sizeId = 0; sizeId < 4; sizeId++)
        {
            var size = 4 << sizeId;
            for (var matrixId = 0; matrixId < 6; matrixId++)
            {
                var matrix = factors.AsSpan(HevcScalingList.GetScalingFactorOffset(sizeId, matrixId), size * size);

                // The first factor is 16, and a factor is the one of the transposed position; the factors of a 4x4 block
                // are all 16 (Table 7-5)
                Assert.Equal(16, matrix[0]);
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        Assert.Equal(matrix[(x * size) + y], matrix[(y * size) + x]);
                        Assert.True(sizeId is not 0 || matrix[(y * size) + x] is 16);
                    }
                }

                // Table 7-6: the last value of the list is the factor of the highest frequencies, 115 for the intra
                // prediction mode and 91 for the inter prediction mode, repeated when the matrix is larger than 8x8
                if (sizeId > 0)
                {
                    Assert.Equal(matrixId < 3 ? 115 : 91, matrix[^1]);
                    Assert.Equal(matrixId < 3 ? 115 : 91, matrix[(size * (size - (size / 8))) + size - (size / 8)]);
                }
            }
        }

        // Without 4:4:4 chroma, the 32x32 matrices of the chroma components do not exist
        Array.Clear(factors);
        HevcScalingList.Default.WriteScalingFactors(factors, chromaArrayType: 1);
        Assert.True(factors.AsSpan(HevcScalingList.GetScalingFactorOffset(3, 1), 1024).IndexOfAnyExcept((byte)0) < 0);
        Assert.Equal(16, factors[HevcScalingList.GetScalingFactorOffset(3, 3)]);
    }
}
