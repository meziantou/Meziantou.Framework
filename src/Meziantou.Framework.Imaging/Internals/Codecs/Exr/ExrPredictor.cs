using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The byte reordering and the delta predictor that RLE, ZIPS and ZIP apply to the uncompressed data of a chunk
/// (compression notes, section 2): the bytes at even offsets, then the bytes at odd offsets, each byte stored as its
/// difference with the previous one plus 128, modulo 256.
/// </summary>
/// <remarks>
/// <see cref="EncodeScalar"/> and <see cref="DecodeScalar"/> are the reference; the vector paths (16 bytes at a time) give
/// the same bytes: the arithmetic is modulo 256 and the reordering moves bytes.
/// </remarks>
internal static class ExrPredictor
{
    private static bool UseVectors => BitConverter.IsLittleEndian && (AdvSimd.Arm64.IsSupported || Ssse3.IsSupported);

    /// <summary>Reorders and predicts the bytes of a chunk.</summary>
    /// <param name="source">The uncompressed data.</param>
    /// <param name="destination">Receives the transformed bytes (as many as <paramref name="source"/>).</param>
    public static void Encode(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (!UseVectors || source.Length < 2 * Vector128<byte>.Count)
        {
            EncodeScalar(source, destination);
            return;
        }

        var length = source.Length;
        destination = destination[..length];
        var half = (length + 1) / 2;
        var pairs = length / 2;
        ref var src = ref unsafe(MemoryMarshal.GetReference(source));
        ref var dst = ref unsafe(MemoryMarshal.GetReference(destination));
        ref var odd = ref unsafe(Unsafe.Add(ref dst, half));
        nuint k = 0;
        for (; k + (nuint)Vector128<byte>.Count <= (nuint)pairs; k += (nuint)Vector128<byte>.Count)
        {
            var low = unsafe(Vector128.LoadUnsafe(ref src, 2 * k));
            var high = unsafe(Vector128.LoadUnsafe(ref src, (2 * k) + (nuint)Vector128<byte>.Count));
            Deinterleave(low, high, out var even, out var odds);
            unsafe { even.StoreUnsafe(ref dst, k); }
            unsafe { odds.StoreUnsafe(ref odd, k); }
        }

        for (; k < (nuint)pairs; k++)
        {
            unsafe { Unsafe.Add(ref dst, k) = Unsafe.Add(ref src, 2 * k); }
            unsafe { Unsafe.Add(ref odd, k) = Unsafe.Add(ref src, (2 * k) + 1); }
        }

        if ((length & 1) != 0)
        {
            destination[half - 1] = source[length - 1];
        }

        // Backward, so that each difference reads the bytes before it unchanged
        var bias = Vector128.Create((byte)128);
        var i = length - Vector128<byte>.Count;
        for (; i >= 1; i -= Vector128<byte>.Count)
        {
            var current = unsafe(Vector128.LoadUnsafe(ref dst, (nuint)i));
            var previous = unsafe(Vector128.LoadUnsafe(ref dst, (nuint)(i - 1)));
            unsafe { (current - previous + bias).StoreUnsafe(ref dst, (nuint)i); }
        }

        for (i += Vector128<byte>.Count - 1; i > 0; i--)
        {
            destination[i] = (byte)(destination[i] - destination[i - 1] + 128);
        }
    }

    /// <summary>The scalar reference of <see cref="Encode"/>.</summary>
    /// <param name="source">The uncompressed data.</param>
    /// <param name="destination">Receives the transformed bytes (as many as <paramref name="source"/>).</param>
    internal static void EncodeScalar(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var length = source.Length;
        var half = (length + 1) / 2;
        for (var k = 0; k < half; k++)
        {
            destination[k] = source[2 * k];
        }

        for (var k = 0; k < length / 2; k++)
        {
            destination[half + k] = source[(2 * k) + 1];
        }

        // Backward, so that each difference reads the byte before it unchanged
        for (var i = length - 1; i > 0; i--)
        {
            destination[i] = (byte)(destination[i] - destination[i - 1] + 128);
        }
    }

    /// <summary>Undoes <see cref="Encode"/>.</summary>
    /// <param name="source">The transformed bytes; overwritten with the reordered bytes.</param>
    /// <param name="destination">Receives the uncompressed data (as many bytes as <paramref name="source"/>).</param>
    public static void Decode(Span<byte> source, Span<byte> destination)
    {
        if (!UseVectors || source.Length < 2 * Vector128<byte>.Count)
        {
            DecodeScalar(source, destination);
            return;
        }

        var length = source.Length;
        destination = destination[..length];
        ref var src = ref unsafe(MemoryMarshal.GetReference(source));
        ref var dst = ref unsafe(MemoryMarshal.GetReference(destination));

        // Each byte is the sum modulo 256 of the first byte and of the differences up to it: the sums of a vector are its
        // shifted copies added for distances 1, 2, 4 and 8, plus the last sum of the vector before
        var bias = Vector128.Create((byte)128);
        var last = Vector128.Create(src);
        nuint i = 1;
        for (; i + (nuint)Vector128<byte>.Count <= (nuint)length; i += (nuint)Vector128<byte>.Count)
        {
            var value = unsafe(Vector128.LoadUnsafe(ref src, i)) - bias;
            value += ShiftBytesUp(value, 1);
            value += ShiftBytesUp(value, 2);
            value += ShiftBytesUp(value, 4);
            value += ShiftBytesUp(value, 8);
            value += last;
            unsafe { value.StoreUnsafe(ref src, i); }
            last = Vector128.Shuffle(value, Vector128.Create((byte)15));
        }

        for (; i < (nuint)length; i++)
        {
            unsafe { Unsafe.Add(ref src, i) = (byte)(Unsafe.Add(ref src, i - 1) + Unsafe.Add(ref src, i) - 128); }
        }

        var half = (length + 1) / 2;
        var pairs = length / 2;
        ref var odd = ref unsafe(Unsafe.Add(ref src, half));
        nuint k = 0;
        for (; k + (nuint)Vector128<byte>.Count <= (nuint)pairs; k += (nuint)Vector128<byte>.Count)
        {
            var even = unsafe(Vector128.LoadUnsafe(ref src, k));
            var odds = unsafe(Vector128.LoadUnsafe(ref odd, k));
            Interleave(even, odds, out var low, out var high);
            unsafe { low.StoreUnsafe(ref dst, 2 * k); }
            unsafe { high.StoreUnsafe(ref dst, (2 * k) + (nuint)Vector128<byte>.Count); }
        }

        for (; k < (nuint)pairs; k++)
        {
            unsafe { Unsafe.Add(ref dst, 2 * k) = Unsafe.Add(ref src, k); }
            unsafe { Unsafe.Add(ref dst, (2 * k) + 1) = Unsafe.Add(ref odd, k); }
        }

        if ((length & 1) != 0)
        {
            destination[length - 1] = source[half - 1];
        }
    }

    /// <summary>The scalar reference of <see cref="Decode"/>.</summary>
    /// <param name="source">The transformed bytes; overwritten with the reordered bytes.</param>
    /// <param name="destination">Receives the uncompressed data (as many bytes as <paramref name="source"/>).</param>
    internal static void DecodeScalar(Span<byte> source, Span<byte> destination)
    {
        var length = source.Length;
        for (var i = 1; i < length; i++)
        {
            source[i] = (byte)(source[i - 1] + source[i] - 128);
        }

        var half = (length + 1) / 2;
        for (var k = 0; k < half; k++)
        {
            destination[2 * k] = source[k];
        }

        for (var k = 0; k < length / 2; k++)
        {
            destination[(2 * k) + 1] = source[half + k];
        }
    }

    /// <summary>Moves the bytes of a vector <paramref name="count"/> lanes up (to higher indices), shifting in zeros.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [SuppressMessage("Performance", "CA1857:A constant is expected for the parameter", Justification = "The callers pass constants, which stay constants once this method is inlined.")]
    private static Vector128<byte> ShiftBytesUp(Vector128<byte> value, [ConstantExpected(Min = 1, Max = 15)] byte count)
    {
        if (Sse2.IsSupported)
            return Sse2.ShiftLeftLogical128BitLane(value, count);

        return AdvSimd.ExtractVector128(Vector128<byte>.Zero, value, (byte)(Vector128<byte>.Count - count));
    }

    /// <summary>Interleaves the bytes of two vectors: <c>a0 b0 a1 b1 ...</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Interleave(Vector128<byte> a, Vector128<byte> b, out Vector128<byte> low, out Vector128<byte> high)
    {
        if (Sse2.IsSupported)
        {
            low = Sse2.UnpackLow(a, b);
            high = Sse2.UnpackHigh(a, b);
            return;
        }

        low = AdvSimd.Arm64.ZipLow(a, b);
        high = AdvSimd.Arm64.ZipHigh(a, b);
    }

    /// <summary>Separates the bytes at even and odd offsets of 32 consecutive bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Deinterleave(Vector128<byte> low, Vector128<byte> high, out Vector128<byte> even, out Vector128<byte> odd)
    {
        if (Sse2.IsSupported)
        {
            var mask = Vector128.Create((ushort)0x00FF);
            even = Sse2.PackUnsignedSaturate((low.AsUInt16() & mask).AsInt16(), (high.AsUInt16() & mask).AsInt16());
            odd = Sse2.PackUnsignedSaturate(Sse2.ShiftRightLogical(low.AsUInt16(), 8).AsInt16(), Sse2.ShiftRightLogical(high.AsUInt16(), 8).AsInt16());
            return;
        }

        even = AdvSimd.Arm64.UnzipEven(low, high);
        odd = AdvSimd.Arm64.UnzipOdd(low, high);
    }
}
