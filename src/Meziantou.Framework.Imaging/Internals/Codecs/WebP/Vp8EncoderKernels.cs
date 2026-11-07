using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The vector kernels of the VP8 encoder's mode decision: a 4x4 block of pixels is one byte vector in column-major order
/// (byte <c>(x * 4) + y</c> is the pixel of row <c>y</c>, column <c>x</c>), which is the input order of the forward DCT
/// (<see cref="Vp8ForwardTransforms.ForwardDctColumns"/>) and the output order of the inverse DCT before its last transpose:
/// a trial (residual, transform, quantization, reconstruction and squared error) stays in vector registers.
/// </summary>
/// <remarks>
/// Every kernel does the integer operations of the scalar code (<see cref="EncodeBlockReference"/> composes the scalar
/// references), so the levels, the reconstructed pixels and the errors are exactly the scalar ones.
/// </remarks>
internal static class Vp8EncoderKernels
{
    private const int MaxLevel = 2048;

    /// <summary>Gets a value indicating whether the vector kernels can be used (128-bit vectors, little-endian lanes).</summary>
    public static bool IsSupported => Vector128.IsHardwareAccelerated && BitConverter.IsLittleEndian;

    // Where the pixels of each sub-block mode come from, in column-major order (the raster table of Vp8Reconstruction,
    // transposed): 0-15 the 3-tap average centered on edge pixel i, 16-31 the 2-tap average of edge pixels i - 16 and i - 15,
    // 32-47 edge pixel i - 32; B_DC_PRED and B_TM_PRED are computed
    private static ReadOnlySpan<byte> SubBlockSources =>
    [
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, // B_VE_PRED
        3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0, // B_HE_PRED
        6, 7, 8, 9, 7, 8, 9, 10, 8, 9, 10, 11, 9, 10, 11, 12, // B_LD_PRED
        4, 3, 2, 1, 5, 4, 3, 2, 6, 5, 4, 3, 7, 6, 5, 4, // B_RD_PRED
        20, 4, 3, 2, 21, 5, 20, 4, 22, 6, 21, 5, 23, 7, 22, 6, // B_VR_PRED
        21, 6, 22, 7, 22, 7, 23, 8, 23, 8, 24, 9, 24, 9, 10, 11, // B_VL_PRED
        19, 18, 17, 16, 4, 3, 2, 1, 5, 19, 18, 17, 6, 4, 3, 2, // B_HD_PRED
        18, 17, 16, 32, 2, 1, 0, 32, 17, 16, 32, 32, 1, 0, 32, 32, // B_HU_PRED
    ];

    // SubBlockSources split for PSHUFB, which selects zero for an index with its high bit set: the indices of the 3-tap
    // averages, then the indices of the 2-tap averages (only B_HU_PRED reads the edge itself)
    private static ReadOnlySpan<byte> SubBlockAverage3Indices =>
    [
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8, // B_VE_PRED
        3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0, // B_HE_PRED
        6, 7, 8, 9, 7, 8, 9, 10, 8, 9, 10, 11, 9, 10, 11, 12, // B_LD_PRED
        4, 3, 2, 1, 5, 4, 3, 2, 6, 5, 4, 3, 7, 6, 5, 4, // B_RD_PRED
        0x80, 4, 3, 2, 0x80, 5, 0x80, 4, 0x80, 6, 0x80, 5, 0x80, 7, 0x80, 6, // B_VR_PRED
        0x80, 6, 0x80, 7, 0x80, 7, 0x80, 8, 0x80, 8, 0x80, 9, 0x80, 9, 10, 11, // B_VL_PRED
        0x80, 0x80, 0x80, 0x80, 4, 3, 2, 1, 5, 0x80, 0x80, 0x80, 6, 4, 3, 2, // B_HD_PRED
        0x80, 0x80, 0x80, 0x80, 2, 1, 0, 0x80, 0x80, 0x80, 0x80, 0x80, 1, 0, 0x80, 0x80, // B_HU_PRED
    ];

    private static ReadOnlySpan<byte> SubBlockAverage2Indices =>
    [
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80,
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, // B_VE_PRED
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, // B_HE_PRED
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, // B_LD_PRED
        0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, // B_RD_PRED
        4, 0x80, 0x80, 0x80, 5, 0x80, 4, 0x80, 6, 0x80, 5, 0x80, 7, 0x80, 6, 0x80, // B_VR_PRED
        5, 0x80, 6, 0x80, 6, 0x80, 7, 0x80, 7, 0x80, 8, 0x80, 8, 0x80, 0x80, 0x80, // B_VL_PRED
        3, 2, 1, 0, 0x80, 0x80, 0x80, 0x80, 0x80, 3, 2, 1, 0x80, 0x80, 0x80, 0x80, // B_HD_PRED
        2, 1, 0, 0x80, 0x80, 0x80, 0x80, 0x80, 1, 0, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, // B_HU_PRED
    ];

    /// <summary>Converts a row of RGBA pixels to luma, as the scalar code of the encoder (BT.601 limited range, 16-bit fixed point).</summary>
    public static void ConvertLumaRow(ReadOnlySpan<byte> rgba, Span<byte> luma)
    {
        var x = 0;
        if (IsSupported)
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(rgba));
            for (; x <= luma.Length - 16; x += 16)
            {
                var y0 = Luma(Vector128.Create(pixels.Slice(x, 4)));
                var y1 = Luma(Vector128.Create(pixels.Slice(x + 4, 4)));
                var y2 = Luma(Vector128.Create(pixels.Slice(x + 8, 4)));
                var y3 = Luma(Vector128.Create(pixels.Slice(x + 12, 4)));
                Vector128.Narrow(Vector128.Narrow(y0, y1), Vector128.Narrow(y2, y3)).CopyTo(luma[x..]);
            }
        }

        for (; x < luma.Length; x++)
        {
            int r = rgba[x * 4], g = rgba[(x * 4) + 1], b = rgba[(x * 4) + 2];
            luma[x] = (byte)(((16829 * r) + (33039 * g) + (6416 * b) + (16 << 16) + 32768) >> 16);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<uint> Luma(Vector128<uint> pixels)
        {
            var mask = Vector128.Create(0xFFu);
            var r = pixels & mask;
            var g = Vector128.ShiftRightLogical(pixels, 8) & mask;
            var b = Vector128.ShiftRightLogical(pixels, 16) & mask;
            return Vector128.ShiftRightLogical((r * 16829) + (g * 33039) + (b * 6416) + Vector128.Create((16u << 16) + 32768), 16);
        }
    }

    /// <summary>Gets a value indicating whether a pixel of <paramref name="rgba"/> is fully transparent.</summary>
    public static bool HasTransparentPixel(ReadOnlySpan<byte> rgba)
    {
        var i = 0;
        if (IsSupported)
        {
            var pixels = unsafe(MemoryMarshal.Cast<byte, uint>(rgba));
            var alpha = Vector128.Create(0xFF000000u);
            for (; i <= pixels.Length - 16; i += 16)
            {
                var transparent = Vector128.Equals(Vector128.Create(pixels.Slice(i, 4)) & alpha, Vector128<uint>.Zero)
                    | Vector128.Equals(Vector128.Create(pixels.Slice(i + 4, 4)) & alpha, Vector128<uint>.Zero)
                    | Vector128.Equals(Vector128.Create(pixels.Slice(i + 8, 4)) & alpha, Vector128<uint>.Zero)
                    | Vector128.Equals(Vector128.Create(pixels.Slice(i + 12, 4)) & alpha, Vector128<uint>.Zero);
                if (transparent != Vector128<uint>.Zero)
                    return true;
            }

            i *= 4;
        }

        for (i += 3; i < rgba.Length; i += 4)
        {
            if (rgba[i] == 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Computes 4 chroma samples from the 8 pixels of two rows when the 4 pixels of each 2x2 block have the same alpha: the
    /// alpha-weighted average of the scalar code is then the plain average, <c>S / 4</c> exactly for the sum <c>S</c> of the
    /// fixed-point components, rounded as <c>floor(S / 2^18 + 1/2)</c> (exact in double precision).
    /// </summary>
    /// <returns><see langword="false"/> (nothing written) when a block has pixels of different alpha values.</returns>
    public static bool TryConvertChroma(ReadOnlySpan<byte> top, ReadOnlySpan<byte> bottom, Span<byte> u, Span<byte> v)
    {
        var topPixels = unsafe(MemoryMarshal.Cast<byte, uint>(top[..32]));
        var bottomPixels = unsafe(MemoryMarshal.Cast<byte, uint>(bottom[..32]));
        var t0 = Vector128.Create(topPixels[..4]);
        var t1 = Vector128.Create(topPixels.Slice(4, 4));
        var b0 = Vector128.Create(bottomPixels[..4]);
        var b1 = Vector128.Create(bottomPixels.Slice(4, 4));

        // Same alpha in each pair of a row, and in both rows
        var swap = Vector128.Create(1u, 0, 3, 2);
        var a0 = Vector128.ShiftRightLogical(t0, 24);
        var a1 = Vector128.ShiftRightLogical(t1, 24);
        var uniform = Vector128.Equals(a0, Vector128.Shuffle(a0, swap)) & Vector128.Equals(a1, Vector128.Shuffle(a1, swap))
            & Vector128.Equals(a0, Vector128.ShiftRightLogical(b0, 24)) & Vector128.Equals(a1, Vector128.ShiftRightLogical(b1, 24));
        if (uniform != Vector128<uint>.AllBitsSet)
            return false;

        Components(t0, out var ut0, out var vt0);
        Components(t1, out var ut1, out var vt1);
        Components(b0, out var ub0, out var vb0);
        Components(b1, out var ub1, out var vb1);
        Store(AddPairs(ut0, ut1) + AddPairs(ub0, ub1), u);
        Store(AddPairs(vt0, vt1) + AddPairs(vb0, vb1), v);
        return true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Components(Vector128<uint> pixels, out Vector128<int> pu, out Vector128<int> pv)
        {
            var mask = Vector128.Create(0xFFu);
            var r = (pixels & mask).AsInt32();
            var g = (Vector128.ShiftRightLogical(pixels, 8) & mask).AsInt32();
            var b = (Vector128.ShiftRightLogical(pixels, 16) & mask).AsInt32();
            pu = (r * -9714) - (g * 19070) + (b * 28784);
            pv = (r * 28784) - (g * 24103) - (b * 4681);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Store(Vector128<int> sum, Span<byte> destination)
        {
            var value = Vector128.ShiftRightArithmetic(sum + Vector128.Create(1 << 17), 18) + Vector128.Create(128);
            value = Vector128.Min(Vector128.Max(value, Vector128<int>.Zero), Vector128.Create(255));
            var bytes = Vector128.Narrow(Vector128.Narrow(value, value).AsUInt16(), Vector128<ushort>.Zero).AsUInt32();
            BinaryPrimitives.WriteUInt32LittleEndian(destination[..4], bytes.ToScalar());
        }
    }

    /// <summary>Adds the adjacent lanes of two vectors: <c>(a0 + a1, a2 + a3, b0 + b1, b2 + b3)</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<int> AddPairs(Vector128<int> a, Vector128<int> b)
    {
        if (AdvSimd.Arm64.IsSupported)
            return AdvSimd.Arm64.AddPairwise(a, b);

        if (Ssse3.IsSupported)
            return Ssse3.HorizontalAdd(a, b);

        return Vector128.Create(a.GetElement(0) + a.GetElement(1), a.GetElement(2) + a.GetElement(3), b.GetElement(0) + b.GetElement(1), b.GetElement(2) + b.GetElement(3));
    }

    /// <summary>Transposes a 4x4 block of bytes (raster to column-major order and back).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> Transpose(Vector128<byte> block)
        => Vector128.Shuffle(block, Vector128.Create((byte)0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15));

    /// <summary>Loads the 4x4 block at <paramref name="offset"/> of a plane in column-major order.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> LoadColumns(ReadOnlySpan<byte> plane, int offset, int stride)
    {
        var rows = Vector128.Create(
            BinaryPrimitives.ReadUInt32LittleEndian(plane.Slice(offset, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(plane.Slice(offset + stride, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(plane.Slice(offset + (2 * stride), 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(plane.Slice(offset + (3 * stride), 4)));
        return Transpose(rows.AsByte());
    }

    /// <summary>Stores a column-major 4x4 block at <paramref name="offset"/> of a plane.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void StoreColumns(Span<byte> plane, int offset, int stride, Vector128<byte> columns)
    {
        var rows = Transpose(columns).AsUInt32();
        BinaryPrimitives.WriteUInt32LittleEndian(plane.Slice(offset, 4), rows.ToScalar());
        BinaryPrimitives.WriteUInt32LittleEndian(plane.Slice(offset + stride, 4), rows.GetElement(1));
        BinaryPrimitives.WriteUInt32LittleEndian(plane.Slice(offset + (2 * stride), 4), rows.GetElement(2));
        BinaryPrimitives.WriteUInt32LittleEndian(plane.Slice(offset + (3 * stride), 4), rows.GetElement(3));
    }

    /// <summary>Computes the forward DCT (raster-order coefficient rows) of the residual of two column-major blocks.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ForwardDct(Vector128<byte> source, Vector128<byte> prediction, out Vector128<int> c0, out Vector128<int> c1, out Vector128<int> c2, out Vector128<int> c3)
    {
        // Columns 0-1 and 2-3 as 16-bit residuals, then one 32-bit vector per column
        var low = Vector128.WidenLower(source).AsInt16() - Vector128.WidenLower(prediction).AsInt16();
        var high = Vector128.WidenUpper(source).AsInt16() - Vector128.WidenUpper(prediction).AsInt16();
        Vp8ForwardTransforms.ForwardDctVector(Vector128.WidenLower(low), Vector128.WidenUpper(low), Vector128.WidenLower(high), Vector128.WidenUpper(high), out c0, out c1, out c2, out c3);
    }

    /// <summary>
    /// Quantizes 16 raster-order coefficients as <see cref="Vp8Encoder.Quantize"/> (single-precision divisions, exact while the
    /// numerators are below 2^24).
    /// </summary>
    /// <returns><see langword="false"/> when a numerator is 2^24 or more: the levels are then not valid.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryQuantize(in BlockQuantizer quantizer, Vector128<int> c0, Vector128<int> c1, Vector128<int> c2, Vector128<int> c3, out Vector128<int> l0, out Vector128<int> l1, out Vector128<int> l2, out Vector128<int> l3)
    {
        // Lane 0 of the first vector is the DC (rounded to nearest), the other lanes AC coefficients (2/3 of a step dead zone)
        var multiplier = Vector128.Create(3);
        var n0 = (Vector128.Abs(c0) * Vector128.Create(1, 3, 3, 3)) + quantizer.OffsetFirst;
        var n1 = (Vector128.Abs(c1) * multiplier) + quantizer.Offset;
        var n2 = (Vector128.Abs(c2) * multiplier) + quantizer.Offset;
        var n3 = (Vector128.Abs(c3) * multiplier) + quantizer.Offset;

        // Numerators from 2^24 (or wrapped to negative values, compared as unsigned) are not exact in single precision
        var limit = Vector128.Create(1u << 24);
        var tooLarge = Vector128.GreaterThanOrEqual(n0.AsUInt32(), limit) | Vector128.GreaterThanOrEqual(n1.AsUInt32(), limit)
            | Vector128.GreaterThanOrEqual(n2.AsUInt32(), limit) | Vector128.GreaterThanOrEqual(n3.AsUInt32(), limit);
        l0 = Level(c0, n0, quantizer.DivisorFirst);
        l1 = Level(c1, n1, quantizer.Divisor);
        l2 = Level(c2, n2, quantizer.Divisor);
        l3 = Level(c3, n3, quantizer.Divisor);
        return tooLarge == Vector128<uint>.Zero;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Level(Vector128<int> coefficient, Vector128<int> numerator, Vector128<float> divisor)
        {
            var quotient = Vector128.ConvertToInt32(Vector128.Floor(Vector128.ConvertToSingle(numerator) / divisor));
            var level = Vector128.Min(quotient, Vector128.Create(MaxLevel));
            return Vector128.ConditionalSelect(Vector128.LessThan(coefficient, Vector128<int>.Zero), -level, level);
        }
    }

    /// <summary>Multiplies levels by their steps, wrapped to 16 bits like the <see langword="short"/> dequantized coefficients.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<int> Dequantize(Vector128<int> levels, Vector128<int> steps)
        => Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(levels * steps, 16), 16);

    /// <summary>
    /// Adds the inverse DCT of dequantized raster-order coefficient rows to a column-major prediction, with the operations of
    /// <see cref="Vp8Transforms.InverseDctAddFull"/> (exact for blocks whose only non-zero coefficient is the DC, too).
    /// </summary>
    /// <returns>The reconstructed block, column-major.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<byte> InverseDctAdd(Vector128<byte> prediction, Vector128<int> r0, Vector128<int> r1, Vector128<int> r2, Vector128<int> r3)
    {
        Pass(ref r0, ref r1, ref r2, ref r3);
        Vp8Transforms.Transpose4x4(ref r0, ref r1, ref r2, ref r3);
        Pass(ref r0, ref r1, ref r2, ref r3);

        // After the horizontal pass, vector x is column x: the column-major order of the prediction
        var four = Vector128.Create(4);
        var low = Vector128.WidenLower(prediction).AsInt16();
        var high = Vector128.WidenUpper(prediction).AsInt16();
        var s0 = Clamp(Vector128.ShiftRightArithmetic(r0 + four, 3) + Vector128.WidenLower(low));
        var s1 = Clamp(Vector128.ShiftRightArithmetic(r1 + four, 3) + Vector128.WidenUpper(low));
        var s2 = Clamp(Vector128.ShiftRightArithmetic(r2 + four, 3) + Vector128.WidenLower(high));
        var s3 = Clamp(Vector128.ShiftRightArithmetic(r3 + four, 3) + Vector128.WidenUpper(high));
        return Vector128.Narrow(Vector128.Narrow(s0, s1).AsUInt16(), Vector128.Narrow(s2, s3).AsUInt16());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Clamp(Vector128<int> value) => Vector128.Min(Vector128.Max(value, Vector128<int>.Zero), Vector128.Create(255));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Pass(ref Vector128<int> i0, ref Vector128<int> i1, ref Vector128<int> i2, ref Vector128<int> i3)
        {
            var a1 = i0 + i2;
            var b1 = i0 - i2;
            var c1 = Vector128.ShiftRightArithmetic(i1 * Vp8Transforms.SinPi8Sqrt2, 16) - (i3 + Vector128.ShiftRightArithmetic(i3 * Vp8Transforms.CosPi8Sqrt2Minus1, 16));
            var d1 = (i1 + Vector128.ShiftRightArithmetic(i1 * Vp8Transforms.CosPi8Sqrt2Minus1, 16)) + Vector128.ShiftRightArithmetic(i3 * Vp8Transforms.SinPi8Sqrt2, 16);
            i0 = a1 + d1;
            i1 = b1 + c1;
            i2 = b1 - c1;
            i3 = a1 - d1;
        }
    }

    /// <summary>
    /// Computes the Y2 coefficient rows from the rows of DC coefficients of the 16 luma blocks (block raster order), as
    /// <see cref="Vp8ForwardTransforms.ForwardWalshHadamard"/>: <c>M D M</c> halved with rounding to nearest, ties away from zero.
    /// </summary>
    public static void ForwardWalshHadamard(Vector128<int> d0, Vector128<int> d1, Vector128<int> d2, Vector128<int> d3, out Vector128<int> y0, out Vector128<int> y1, out Vector128<int> y2, out Vector128<int> y3)
    {
        // M D: the rows of M are (1, 1, 1, 1), (1, 1, -1, -1), (1, -1, -1, 1) and (1, -1, 1, -1)
        var t0 = d0 + d1 + d2 + d3;
        var t1 = d0 + d1 - d2 - d3;
        var t2 = d0 - d1 - d2 + d3;
        var t3 = d0 - d1 + d2 - d3;

        // (M D) M: M is symmetric, so the columns are the same sums of the transposed rows
        Vp8Transforms.Transpose4x4(ref t0, ref t1, ref t2, ref t3);
        y0 = Halve(t0 + t1 + t2 + t3);
        y1 = Halve(t0 + t1 - t2 - t3);
        y2 = Halve(t0 - t1 - t2 + t3);
        y3 = Halve(t0 - t1 + t2 - t3);
        Vp8Transforms.Transpose4x4(ref y0, ref y1, ref y2, ref y3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Halve(Vector128<int> sum)
        {
            var magnitude = Vector128.ShiftRightArithmetic(Vector128.Abs(sum) + Vector128<int>.One, 1);
            return Vector128.ConditionalSelect(Vector128.LessThan(sum, Vector128<int>.Zero), -magnitude, magnitude);
        }
    }

    /// <summary>
    /// Inverts the Walsh-Hadamard transform of dequantized Y2 rows into the DC coefficients of the 16 luma blocks (block raster
    /// order, wrapped to 16 bits), as <see cref="Vp8Transforms.InverseWalshHadamard"/>.
    /// </summary>
    public static void InverseWalshHadamard(Vector128<int> i0, Vector128<int> i1, Vector128<int> i2, Vector128<int> i3, out Vector128<int> o0, out Vector128<int> o1, out Vector128<int> o2, out Vector128<int> o3)
    {
        // The vertical pass on the 4 columns at once, then the horizontal pass on the 4 rows at once (transposed)
        var a1 = i0 + i3;
        var b1 = i1 + i2;
        var c1 = i1 - i2;
        var d1 = i0 - i3;
        var t0 = a1 + b1;
        var t1 = c1 + d1;
        var t2 = a1 - b1;
        var t3 = d1 - c1;
        Vp8Transforms.Transpose4x4(ref t0, ref t1, ref t2, ref t3);
        a1 = t0 + t3;
        b1 = t1 + t2;
        c1 = t1 - t2;
        d1 = t0 - t3;
        var three = Vector128.Create(3);
        o0 = Wrap(Vector128.ShiftRightArithmetic(a1 + b1 + three, 3));
        o1 = Wrap(Vector128.ShiftRightArithmetic(c1 + d1 + three, 3));
        o2 = Wrap(Vector128.ShiftRightArithmetic(a1 - b1 + three, 3));
        o3 = Wrap(Vector128.ShiftRightArithmetic(d1 - c1 + three, 3));
        Vp8Transforms.Transpose4x4(ref o0, ref o1, ref o2, ref o3);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static Vector128<int> Wrap(Vector128<int> value) => Vector128.ShiftRightArithmetic(Vector128.ShiftLeft(value, 16), 16);
    }

    /// <summary>Computes the sum of the squared differences of two blocks of 16 pixels.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SquaredError(Vector128<byte> a, Vector128<byte> b) => (int)Vector128.Sum(SquaredErrors(a, b));

    /// <summary>Computes the squared differences of 16 pixels, summed into 4 lanes (each at most 4 * 255^2).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector128<uint> SquaredErrors(Vector128<byte> a, Vector128<byte> b)
    {
        // |a - b| fits a byte, its square 16 bits
        if (AdvSimd.IsSupported)
        {
            var difference = AdvSimd.AbsoluteDifference(a, b);
            var low = AdvSimd.MultiplyWideningLower(difference.GetLower(), difference.GetLower());
            var high = AdvSimd.MultiplyWideningUpper(difference, difference);
            return AdvSimd.AddPairwiseWideningAndAdd(AdvSimd.AddPairwiseWidening(low), high);
        }

        var absolute = Vector128.SubtractSaturate(a, b) | Vector128.SubtractSaturate(b, a);
        var lowWords = Vector128.WidenLower(absolute).AsInt16();
        var highWords = Vector128.WidenUpper(absolute).AsInt16();
        if (Sse2.IsSupported)
            return (Sse2.MultiplyAddAdjacent(lowWords, lowWords) + Sse2.MultiplyAddAdjacent(highWords, highWords)).AsUInt32();

        var l0 = Vector128.WidenLower(lowWords);
        var l1 = Vector128.WidenUpper(lowWords);
        var h0 = Vector128.WidenLower(highWords);
        var h1 = Vector128.WidenUpper(highWords);
        return ((l0 * l0) + (l1 * l1) + (h0 * h0) + (h1 * h1)).AsUInt32();
    }

    /// <summary>Computes the sum of the squared differences of the first <paramref name="width"/> x <paramref name="height"/> samples of two planes.</summary>
    public static long PlaneSquaredError(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b, int stride, int width, int height)
    {
        long sum = 0;
        for (var y = 0; y < height; y++)
        {
            var rowA = a.Slice(y * stride, width);
            var rowB = b.Slice(y * stride, width);
            var x = 0;
            if (Vector128.IsHardwareAccelerated)
            {
                // At most 4 * 255^2 per lane and vector: a row of 16,383 samples stays below 2^32 per lane
                var lanes = Vector128<uint>.Zero;
                for (; x <= width - Vector128<byte>.Count; x += Vector128<byte>.Count)
                {
                    lanes += SquaredErrors(Vector128.Create(rowA.Slice(x, Vector128<byte>.Count)), Vector128.Create(rowB.Slice(x, Vector128<byte>.Count)));
                }

                sum += (long)lanes.ToScalar() + lanes.GetElement(1) + lanes.GetElement(2) + lanes.GetElement(3);
            }

            for (; x < width; x++)
            {
                var difference = rowA[x] - rowB[x];
                sum += difference * difference;
            }
        }

        return sum;
    }

    /// <summary>
    /// Quantizes and reconstructs one 4x4 block (without Y2) from its source and prediction, column-major, as the scalar
    /// path does (<see cref="EncodeBlockReference"/>).
    /// </summary>
    /// <returns><see langword="false"/> when the vector quantization is not exact for the block (nothing valid is written).</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryEncodeBlock(Vector128<byte> source, Vector128<byte> prediction, in BlockQuantizer quantizer, out Vector128<short> levelsLow, out Vector128<short> levelsHigh, out Vector128<byte> reconstruction)
    {
        ForwardDct(source, prediction, out var c0, out var c1, out var c2, out var c3);
        var exact = TryQuantize(quantizer, c0, c1, c2, c3, out var l0, out var l1, out var l2, out var l3);
        levelsLow = Vector128.Narrow(l0, l1);
        levelsHigh = Vector128.Narrow(l2, l3);
        reconstruction = InverseDctAdd(prediction, Dequantize(l0, quantizer.StepFirst), Dequantize(l1, quantizer.Step), Dequantize(l2, quantizer.Step), Dequantize(l3, quantizer.Step));
        return exact;
    }

    /// <summary>The scalar reference of <see cref="TryEncodeBlock"/> (and its fallback): the steps of the scalar encoder path.</summary>
    public static void EncodeBlockReference(Vector128<byte> source, Vector128<byte> prediction, int dcStep, int acStep, out Vector128<short> levelsLow, out Vector128<short> levelsHigh, out Vector128<byte> reconstruction)
    {
        Span<byte> sourcePixels = stackalloc byte[16];
        Span<byte> pixels = stackalloc byte[16];
        Transpose(source).CopyTo(sourcePixels);
        Transpose(prediction).CopyTo(pixels);
        Span<int> residual = stackalloc int[16];
        for (var i = 0; i < 16; i++)
        {
            residual[i] = sourcePixels[i] - pixels[i];
        }

        Span<int> coefficients = stackalloc int[16];
        Vp8ForwardTransforms.ForwardDct(residual, coefficients);
        Span<short> levels = stackalloc short[16];
        Vp8Encoder.QuantizeScalar(coefficients, levels, dcStep, acStep, first: 0);
        Span<short> dequantized = stackalloc short[16];
        for (var i = 0; i < 16; i++)
        {
            dequantized[i] = (short)(levels[i] * (i == 0 ? dcStep : acStep));
        }

        Vp8Transforms.InverseDctAdd(dequantized, pixels, 4);
        levelsLow = Vector128.Create((ReadOnlySpan<short>)levels[..8]);
        levelsHigh = Vector128.Create((ReadOnlySpan<short>)levels.Slice(8, 8));
        reconstruction = Transpose(Vector128.Create((ReadOnlySpan<byte>)pixels));
    }

    /// <summary>Computes the column-major predictions of a sub-block from its 13 edge pixels (<see cref="Vp8Reconstruction.GetSubBlockEdge"/>).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct SubBlockPredictor
    {
        private readonly Vector128<byte> _edge;
        private readonly Vector128<byte> _average3;
        private readonly Vector128<byte> _average2;
        private readonly Vector128<byte> _dc;

        public SubBlockPredictor(ReadOnlySpan<byte> edge)
        {
            var sum = 4;
            for (var i = 0; i < 4; i++)
            {
                sum += edge[5 + i] + edge[3 - i];
            }

            _dc = Vector128.Create((byte)(sum >> 3));
            _edge = Vector128.Create(edge[..Vp8Reconstruction.SubBlockEdgeLength]);

            // (e[i - 1] + 2 e[i] + e[i + 1] + 2) >> 2 (the first and the last pixel repeat) is the rounded average of e[i] and
            // the truncated average of its neighbors
            var previous = Vector128.Shuffle(_edge, Vector128.Create((byte)0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 12, 12));
            var next = Vector128.Shuffle(_edge, Vector128.Create((byte)1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 12, 12, 12, 12));
            _average3 = RoundedAverage(_edge, TruncatedAverage(previous, next));
            _average2 = RoundedAverage(_edge, next);
        }

        /// <summary>Gets the column-major prediction of a sub-block mode.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vector128<byte> Predict(int mode)
        {
            if (mode == Vp8Tables.BDcPred)
                return _dc;

            if (mode == Vp8Tables.BTmPred)
            {
                // A[x] + L[y] - P, clamped
                var above = Vector128.Shuffle(_edge, Vector128.Create((byte)5, 5, 5, 5, 6, 6, 6, 6, 7, 7, 7, 7, 8, 8, 8, 8));
                var left = Vector128.Shuffle(_edge, Vector128.Create((byte)3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0, 3, 2, 1, 0));
                var corner = Vector128.Shuffle(_edge, Vector128.Create((byte)4));
                return Vector128.SubtractSaturate(Vector128.AddSaturate(above, Vector128.SubtractSaturate(left, corner)), Vector128.SubtractSaturate(corner, left));
            }

            if (AdvSimd.Arm64.IsSupported)
                return AdvSimd.Arm64.VectorTableLookup((_average3, _average2, _edge), Vector128.Create(SubBlockSources.Slice(mode * 16, 16)));

            if (Ssse3.IsSupported)
            {
                var result = Ssse3.Shuffle(_average3, Vector128.Create(SubBlockAverage3Indices.Slice(mode * 16, 16))) | Ssse3.Shuffle(_average2, Vector128.Create(SubBlockAverage2Indices.Slice(mode * 16, 16)));
                if (mode == Vp8Tables.BHuPred)
                {
                    // The bottom-right pixels repeat L3 (edge pixel 0)
                    result |= Ssse3.Shuffle(_edge, Vector128.Create((byte)0x80, 0x80, 0x80, 0, 0x80, 0x80, 0x80, 0, 0x80, 0x80, 0, 0, 0x80, 0x80, 0, 0));
                }

                return result;
            }

            // An index out of the 0-15 range selects zero
            var indices = Vector128.Create(SubBlockSources.Slice(mode * 16, 16));
            return Vector128.Shuffle(_average3, indices) | Vector128.Shuffle(_average2, indices - Vector128.Create((byte)16)) | Vector128.Shuffle(_edge, indices - Vector128.Create((byte)32));
        }

        /// <summary>(a + b + 1) &gt;&gt; 1 per byte.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> RoundedAverage(Vector128<byte> a, Vector128<byte> b)
        {
            if (AdvSimd.IsSupported)
                return AdvSimd.FusedAddRoundedHalving(a, b);

            if (Sse2.IsSupported)
                return Sse2.Average(a, b);

            return (a | b) - Vector128.ShiftRightLogical(a ^ b, 1);
        }

        /// <summary>(a + b) &gt;&gt; 1 per byte.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector128<byte> TruncatedAverage(Vector128<byte> a, Vector128<byte> b)
        {
            if (AdvSimd.IsSupported)
                return AdvSimd.FusedAddHalving(a, b);

            return (a & b) + Vector128.ShiftRightLogical(a ^ b, 1);
        }
    }

    /// <summary>The quantizer of one block type in vectors: rounding offsets, divisors and steps (DC in lane 0 of the first row).</summary>
    [StructLayout(LayoutKind.Auto)]
    public readonly struct BlockQuantizer
    {
        public BlockQuantizer(int dcStep, int acStep)
        {
            OffsetFirst = Vector128.Create(dcStep >> 1, acStep, acStep, acStep);
            Offset = Vector128.Create(acStep);
            DivisorFirst = Vector128.Create((float)dcStep, 3 * acStep, 3 * acStep, 3 * acStep);
            Divisor = Vector128.Create((float)(3 * acStep));
            StepFirst = Vector128.Create(dcStep, acStep, acStep, acStep);
            Step = Vector128.Create(acStep);
        }

        public Vector128<int> OffsetFirst { get; }

        public Vector128<int> Offset { get; }

        public Vector128<float> DivisorFirst { get; }

        public Vector128<float> Divisor { get; }

        public Vector128<int> StepFirst { get; }

        public Vector128<int> Step { get; }
    }
}
