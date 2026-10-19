using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The vector operations of the HEVC kernels that the cross-platform <see cref="Vector128"/> API does not express: the
/// AdvSimd instructions on Arm64, the SSE2 ones on x64. Both compute the same lanes. The kernels use them when
/// <see cref="IsSupported"/> is true and run their scalar reference otherwise.
/// </summary>
internal static class HevcSimd
{
    /// <summary>Whether the operations are hardware accelerated.</summary>
    public static bool IsSupported => AdvSimd.Arm64.IsSupported || Sse41.IsSupported;

    /// <summary>An 8x8 transposition of 16-bit lanes, in place: the lane i of the vector j becomes the lane j of the vector i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transpose8x8(ref Vector128<short> r0, ref Vector128<short> r1, ref Vector128<short> r2, ref Vector128<short> r3, ref Vector128<short> r4, ref Vector128<short> r5, ref Vector128<short> r6, ref Vector128<short> r7)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            // Pairs of 16-bit lanes, then of 32-bit lanes, then of 64-bit lanes
            var t0 = AdvSimd.Arm64.TransposeEven(r0, r1).AsInt32();
            var t1 = AdvSimd.Arm64.TransposeOdd(r0, r1).AsInt32();
            var t2 = AdvSimd.Arm64.TransposeEven(r2, r3).AsInt32();
            var t3 = AdvSimd.Arm64.TransposeOdd(r2, r3).AsInt32();
            var t4 = AdvSimd.Arm64.TransposeEven(r4, r5).AsInt32();
            var t5 = AdvSimd.Arm64.TransposeOdd(r4, r5).AsInt32();
            var t6 = AdvSimd.Arm64.TransposeEven(r6, r7).AsInt32();
            var t7 = AdvSimd.Arm64.TransposeOdd(r6, r7).AsInt32();
            var u0 = AdvSimd.Arm64.TransposeEven(t0, t2).AsInt64();
            var u2 = AdvSimd.Arm64.TransposeOdd(t0, t2).AsInt64();
            var u1 = AdvSimd.Arm64.TransposeEven(t1, t3).AsInt64();
            var u3 = AdvSimd.Arm64.TransposeOdd(t1, t3).AsInt64();
            var u4 = AdvSimd.Arm64.TransposeEven(t4, t6).AsInt64();
            var u6 = AdvSimd.Arm64.TransposeOdd(t4, t6).AsInt64();
            var u5 = AdvSimd.Arm64.TransposeEven(t5, t7).AsInt64();
            var u7 = AdvSimd.Arm64.TransposeOdd(t5, t7).AsInt64();
            r0 = AdvSimd.Arm64.TransposeEven(u0, u4).AsInt16();
            r4 = AdvSimd.Arm64.TransposeOdd(u0, u4).AsInt16();
            r1 = AdvSimd.Arm64.TransposeEven(u1, u5).AsInt16();
            r5 = AdvSimd.Arm64.TransposeOdd(u1, u5).AsInt16();
            r2 = AdvSimd.Arm64.TransposeEven(u2, u6).AsInt16();
            r6 = AdvSimd.Arm64.TransposeOdd(u2, u6).AsInt16();
            r3 = AdvSimd.Arm64.TransposeEven(u3, u7).AsInt16();
            r7 = AdvSimd.Arm64.TransposeOdd(u3, u7).AsInt16();
        }
        else
        {
            // Lanes 0-3 and 4-7 of pairs of rows, then pairs of columns of 4 rows, then the columns
            var a0 = Sse2.UnpackLow(r0, r1).AsInt32();
            var a1 = Sse2.UnpackHigh(r0, r1).AsInt32();
            var a2 = Sse2.UnpackLow(r2, r3).AsInt32();
            var a3 = Sse2.UnpackHigh(r2, r3).AsInt32();
            var a4 = Sse2.UnpackLow(r4, r5).AsInt32();
            var a5 = Sse2.UnpackHigh(r4, r5).AsInt32();
            var a6 = Sse2.UnpackLow(r6, r7).AsInt32();
            var a7 = Sse2.UnpackHigh(r6, r7).AsInt32();
            var b0 = Sse2.UnpackLow(a0, a2).AsInt64();
            var b1 = Sse2.UnpackHigh(a0, a2).AsInt64();
            var b2 = Sse2.UnpackLow(a1, a3).AsInt64();
            var b3 = Sse2.UnpackHigh(a1, a3).AsInt64();
            var b4 = Sse2.UnpackLow(a4, a6).AsInt64();
            var b5 = Sse2.UnpackHigh(a4, a6).AsInt64();
            var b6 = Sse2.UnpackLow(a5, a7).AsInt64();
            var b7 = Sse2.UnpackHigh(a5, a7).AsInt64();
            r0 = Sse2.UnpackLow(b0, b4).AsInt16();
            r1 = Sse2.UnpackHigh(b0, b4).AsInt16();
            r2 = Sse2.UnpackLow(b1, b5).AsInt16();
            r3 = Sse2.UnpackHigh(b1, b5).AsInt16();
            r4 = Sse2.UnpackLow(b2, b6).AsInt16();
            r5 = Sse2.UnpackHigh(b2, b6).AsInt16();
            r6 = Sse2.UnpackLow(b3, b7).AsInt16();
            r7 = Sse2.UnpackHigh(b3, b7).AsInt16();
        }
    }

    /// <summary>A 4x4 transposition of 32-bit lanes, in place: the lane i of the vector j becomes the lane j of the vector i.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Transpose4x4(ref Vector128<int> r0, ref Vector128<int> r1, ref Vector128<int> r2, ref Vector128<int> r3)
    {
        if (AdvSimd.Arm64.IsSupported)
        {
            var t0 = AdvSimd.Arm64.TransposeEven(r0, r1).AsInt64();
            var t1 = AdvSimd.Arm64.TransposeOdd(r0, r1).AsInt64();
            var t2 = AdvSimd.Arm64.TransposeEven(r2, r3).AsInt64();
            var t3 = AdvSimd.Arm64.TransposeOdd(r2, r3).AsInt64();
            r0 = AdvSimd.Arm64.TransposeEven(t0, t2).AsInt32();
            r1 = AdvSimd.Arm64.TransposeEven(t1, t3).AsInt32();
            r2 = AdvSimd.Arm64.TransposeOdd(t0, t2).AsInt32();
            r3 = AdvSimd.Arm64.TransposeOdd(t1, t3).AsInt32();
        }
        else
        {
            var t0 = Sse2.UnpackLow(r0, r1).AsInt64();
            var t1 = Sse2.UnpackLow(r2, r3).AsInt64();
            var t2 = Sse2.UnpackHigh(r0, r1).AsInt64();
            var t3 = Sse2.UnpackHigh(r2, r3).AsInt64();
            r0 = Sse2.UnpackLow(t0, t1).AsInt32();
            r1 = Sse2.UnpackHigh(t0, t1).AsInt32();
            r2 = Sse2.UnpackLow(t2, t3).AsInt32();
            r3 = Sse2.UnpackHigh(t2, t3).AsInt32();
        }
    }
}
