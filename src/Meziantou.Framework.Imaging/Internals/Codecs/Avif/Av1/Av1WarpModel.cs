using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The derivation of the warp models of the AV1 inter prediction (AV1 specification, sections 7.11.3.6 "Setup shear process",
/// 7.11.3.7 "Resolve divisor process" and 7.11.3.8 "Warp estimation process").
/// </summary>
internal static class Av1WarpModel
{
    /// <summary>Derives the two shears that make an affine transformation.</summary>
    /// <param name="warpParams">The 6 parameters of the warp model.</param>
    /// <param name="shear">Receives alpha, beta, gamma and delta.</param>
    /// <returns><see langword="true"/> when the model can be used by the block warp process (<c>warpValid</c>).</returns>
    public static bool SetupShear(ReadOnlySpan<int> warpParams, Span<int> shear)
    {
        // A model whose scale is not positive has no shear decomposition
        if (warpParams[2] <= 0)
            return false;

        var alpha0 = Math.Clamp(warpParams[2] - (1 << Av1Constants.WarpedmodelPrecBits), -32768, 32767);
        var beta0 = Math.Clamp(warpParams[3], -32768, 32767);
        ResolveDivisor(warpParams[2], out var divShift, out var divFactor);
        var v = (long)warpParams[4] << Av1Constants.WarpedmodelPrecBits;
        var gamma0 = (int)Math.Clamp(Round2Signed(v * divFactor, divShift), -32768, 32767);
        var w = (long)warpParams[3] * warpParams[4];
        var delta0 = (int)Math.Clamp(warpParams[5] - Round2Signed(w * divFactor, divShift) - (1 << Av1Constants.WarpedmodelPrecBits), -32768, 32767);
        var alpha = Reduce(alpha0);
        var beta = Reduce(beta0);
        var gamma = Reduce(gamma0);
        var delta = Reduce(delta0);
        shear[0] = alpha;
        shear[1] = beta;
        shear[2] = gamma;
        shear[3] = delta;
        if ((4 * Math.Abs(alpha)) + (7 * Math.Abs(beta)) >= 1 << Av1Constants.WarpedmodelPrecBits)
            return false;

        return (4 * Math.Abs(gamma)) + (4 * Math.Abs(delta)) < 1 << Av1Constants.WarpedmodelPrecBits;

        static int Reduce(int value) => (int)Round2Signed(value, Av1Constants.WarpParamReduceBits) << Av1Constants.WarpParamReduceBits;
    }

    /// <summary>
    /// Fits a warp model to the motion of the neighbors of a block by least squares.
    /// </summary>
    /// <param name="candList">The samples (<c>CandList</c>): for each one, the row and the column of its source then of its destination, in units of 1/8 luma sample.</param>
    /// <param name="numSamples">The number of samples.</param>
    /// <param name="midY">The row of the center of the block, in luma samples.</param>
    /// <param name="midX">The column of the center of the block, in luma samples.</param>
    /// <param name="mvRow">The row of the motion vector of the block.</param>
    /// <param name="mvCol">The column of the motion vector of the block.</param>
    /// <param name="localWarpParams">Receives the 6 parameters of the model (<c>LocalWarpParams</c>).</param>
    /// <returns><see langword="true"/> when the fit succeeded (<c>LocalValid</c>).</returns>
    public static bool Estimate(in InlineArray8<InlineArray4<int>> candList, int numSamples, int midY, int midX, int mvRow, int mvCol, Span<int> localWarpParams)
    {
        long a00 = 0;
        long a01 = 0;
        long a11 = 0;
        long bx0 = 0;
        long bx1 = 0;
        long by0 = 0;
        long by1 = 0;
        var suy = midY * 8;
        var sux = midX * 8;
        var duy = suy + mvRow;
        var dux = sux + mvCol;
        for (var i = 0; i < numSamples; i++)
        {
            var sy = candList[i][0] - suy;
            var sx = candList[i][1] - sux;
            var dy = candList[i][2] - duy;
            var dx = candList[i][3] - dux;
            if (Math.Abs(sx - dx) < Av1Constants.LsMvMax && Math.Abs(sy - dy) < Av1Constants.LsMvMax)
            {
                a00 += LsProduct(sx, sx) + 8;
                a01 += LsProduct(sx, sy) + 4;
                a11 += LsProduct(sy, sy) + 8;
                bx0 += LsProduct(sx, dx) + 8;
                bx1 += LsProduct(sy, dx) + 4;
                by0 += LsProduct(sx, dy) + 4;
                by1 += LsProduct(sy, dy) + 8;
            }
        }

        var det = (a00 * a11) - (a01 * a01);
        if (det is 0)
            return false;

        ResolveDivisor(det, out var divShift, out var divFactor);
        divShift -= Av1Constants.WarpedmodelPrecBits;
        long factor = divFactor;
        if (divShift < 0)
        {
            factor <<= -divShift;
            divShift = 0;
        }

        const int Clamp = Av1Constants.WarpedmodelNondiagaffineClamp;
        const int One = 1 << Av1Constants.WarpedmodelPrecBits;
        localWarpParams[2] = Divide((a11 * bx0) - (a01 * bx1), factor, divShift, One - Clamp + 1, One + Clamp - 1);
        localWarpParams[3] = Divide((-a01 * bx0) + (a00 * bx1), factor, divShift, -Clamp + 1, Clamp - 1);
        localWarpParams[4] = Divide((a11 * by0) - (a01 * by1), factor, divShift, -Clamp + 1, Clamp - 1);
        localWarpParams[5] = Divide((-a01 * by0) + (a00 * by1), factor, divShift, One - Clamp + 1, One + Clamp - 1);
        var vx = ((long)mvCol * (1 << (Av1Constants.WarpedmodelPrecBits - 3))) - (((long)midX * (localWarpParams[2] - One)) + ((long)midY * localWarpParams[3]));
        var vy = ((long)mvRow * (1 << (Av1Constants.WarpedmodelPrecBits - 3))) - (((long)midX * localWarpParams[4]) + ((long)midY * (localWarpParams[5] - One)));
        localWarpParams[0] = (int)Math.Clamp(vx, -Av1Constants.WarpedmodelTransClamp, Av1Constants.WarpedmodelTransClamp - 1);
        localWarpParams[1] = (int)Math.Clamp(vy, -Av1Constants.WarpedmodelTransClamp, Av1Constants.WarpedmodelTransClamp - 1);
        return true;

        static long LsProduct(long a, long b) => ((a * b) >> 2) + (a + b);

        // The product does not always fit in 64 bits
        static int Divide(long v, long factor, int shift, int min, int max)
        {
            var product = (Int128)v * factor;
            Int128 result;
            if (shift is 0)
            {
                result = product;
            }
            else
            {
                var half = Int128.One << (shift - 1);
                result = product >= 0 ? (product + half) >> shift : -((-product + half) >> shift);
            }

            return (int)Int128.Clamp(result, min, max);
        }
    }

    // Section 7.11.3.7 "Resolve divisor process"
    private static void ResolveDivisor(long d, out int divShift, out int divFactor)
    {
        var magnitude = (ulong)Math.Abs(d);
        var n = BitOperations.Log2(magnitude);
        var e = (long)(magnitude - (1UL << n));
        long f;
        if (n > Av1Constants.DivLutBits)
        {
            var bits = n - Av1Constants.DivLutBits;
            f = (e + (1L << (bits - 1))) >> bits;
        }
        else
        {
            f = e << (Av1Constants.DivLutBits - n);
        }

        divShift = n + Av1Constants.DivLutPrecBits;
        divFactor = d < 0 ? -Av1Tables.DivLut[(int)f] : Av1Tables.DivLut[(int)f];
    }

    private static long Round2Signed(long value, int bits) => value >= 0 ? (value + (1L << (bits - 1))) >> bits : -((-value + (1L << (bits - 1))) >> bits);
}
