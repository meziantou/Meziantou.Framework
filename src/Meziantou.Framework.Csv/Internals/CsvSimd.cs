using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;

namespace Meziantou.Framework.Csv.Internals;

/// <summary>Finds the characters that matter to the CSV grammar (separator, quote, CR and LF) in blocks of 16, 32 or 64 elements.</summary>
/// <remarks>
/// UTF-16 code units are narrowed to bytes before the comparison. On x86, the pack instructions saturate them as signed values: U+0100 to U+7FFF
/// become 0xFF and U+8000 to U+FFFF become 0x00. Elsewhere, they are saturated to 0xFF. The parser uses the scalar path when the separator or the
/// quote is U+0000 or above U+00FE, so the masks are exact and the parser does not read the elements they report.
/// </remarks>
internal static class CsvSimd
{
    // On Arm64, the 64-element blocks are four 128-bit vectors (see MatchArm64): building a 64-bit mask from them costs less per element than
    // a 16-bit mask from one vector, which has no movemask instruction
    public static int DefaultVectorSize { get; } =
        Vector512.IsHardwareAccelerated || AdvSimd.Arm64.IsSupported ? 64 :
        Vector256.IsHardwareAccelerated ? 32 :
        Vector128.IsHardwareAccelerated ? 16 :
        0;

    public static int Resolve(int vectorSize) => vectorSize switch
    {
        < 0 => DefaultVectorSize,
        0 or 16 or 32 or 64 => vectorSize,
        _ => throw new ArgumentOutOfRangeException(nameof(vectorSize)),
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void GetMasks<T>(ref T start, int vectorSize, byte separator, byte quote, out ulong separators, out ulong specials)
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            ref var bytes = ref unsafe(Unsafe.As<T, byte>(ref start));
            switch (vectorSize)
            {
                case 64 when AdvSimd.Arm64.IsSupported:
                    MatchArm64(
                        unsafe(Vector128.LoadUnsafe(ref bytes)),
                        unsafe(Vector128.LoadUnsafe(ref bytes, 16)),
                        unsafe(Vector128.LoadUnsafe(ref bytes, 32)),
                        unsafe(Vector128.LoadUnsafe(ref bytes, 48)),
                        separator, quote, out separators, out specials);
                    return;

                case 64:
                    Match(unsafe(Vector512.LoadUnsafe(ref bytes)), separator, quote, out separators, out specials);
                    return;

                case 32:
                    Match(unsafe(Vector256.LoadUnsafe(ref bytes)), separator, quote, out separators, out specials);
                    return;

                default:
                    Match(unsafe(Vector128.LoadUnsafe(ref bytes)), separator, quote, out separators, out specials);
                    return;
            }
        }

        ref var chars = ref unsafe(Unsafe.As<T, ushort>(ref start));
        switch (vectorSize)
        {
            case 64 when AdvSimd.Arm64.IsSupported:
                MatchArm64(
                    NarrowArm64(ref chars, 0),
                    NarrowArm64(ref chars, 16),
                    NarrowArm64(ref chars, 32),
                    NarrowArm64(ref chars, 48),
                    separator, quote, out separators, out specials);
                return;

            case 64:
                {
                    if (Avx512BW.IsSupported)
                    {
                        // The pack orders the 128-bit lanes of the two vectors alternately: the permutation restores the order of the elements
                        var packed = Avx512BW.PackUnsignedSaturate(unsafe(Vector512.LoadUnsafe(ref chars)).AsInt16(), unsafe(Vector512.LoadUnsafe(ref chars, 32)).AsInt16());
                        Match(Avx512F.PermuteVar8x64(packed.AsInt64(), Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7)).AsByte(), separator, quote, out separators, out specials);
                        return;
                    }

                    var max = Vector512.Create((ushort)0xFF);
                    var value = Vector512.Narrow(Vector512.Min(unsafe(Vector512.LoadUnsafe(ref chars)), max), Vector512.Min(unsafe(Vector512.LoadUnsafe(ref chars, 32)), max));
                    Match(value, separator, quote, out separators, out specials);
                    return;
                }

            case 32:
                {
                    if (Avx2.IsSupported)
                    {
                        var packed = Avx2.PackUnsignedSaturate(unsafe(Vector256.LoadUnsafe(ref chars)).AsInt16(), unsafe(Vector256.LoadUnsafe(ref chars, 16)).AsInt16());
                        Match(Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte(), separator, quote, out separators, out specials);
                        return;
                    }

                    var max = Vector256.Create((ushort)0xFF);
                    var value = Vector256.Narrow(Vector256.Min(unsafe(Vector256.LoadUnsafe(ref chars)), max), Vector256.Min(unsafe(Vector256.LoadUnsafe(ref chars, 16)), max));
                    Match(value, separator, quote, out separators, out specials);
                    return;
                }

            default:
                {
                    if (Sse2.IsSupported)
                    {
                        Match(Sse2.PackUnsignedSaturate(unsafe(Vector128.LoadUnsafe(ref chars)).AsInt16(), unsafe(Vector128.LoadUnsafe(ref chars, 8)).AsInt16()), separator, quote, out separators, out specials);
                        return;
                    }

                    var max = Vector128.Create((ushort)0xFF);
                    var value = Vector128.Narrow(Vector128.Min(unsafe(Vector128.LoadUnsafe(ref chars)), max), Vector128.Min(unsafe(Vector128.LoadUnsafe(ref chars, 8)), max));
                    Match(value, separator, quote, out separators, out specials);
                    return;
                }
        }
    }

    /// <summary>Copies a value that has no separator, quote, CR or LF to <paramref name="destination"/>.</summary>
    /// <returns><see langword="false"/> when the value has one of these characters: the content of <paramref name="destination"/> is then undefined.</returns>
    /// <remarks>
    /// The writer checks most values before it copies them. Checking each block while it is copied reads the value once, and saves the call to the
    /// search: about 20% faster than <c>ContainsAny</c> with <see cref="System.Buffers.SearchValues{T}"/> then <c>CopyTo</c> for the short values of
    /// most CSV data. <paramref name="destination"/> must have at least the length of <paramref name="value"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCopyPlain<T>(ReadOnlySpan<T> value, Span<T> destination, T separator, T quote)
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
            return TryCopyPlainCore(unsafe(MemoryMarshal.Cast<T, byte>(value)), unsafe(MemoryMarshal.Cast<T, byte>(destination)), unsafe(Unsafe.As<T, byte>(ref separator)), unsafe(Unsafe.As<T, byte>(ref quote)));

        return TryCopyPlainCore(unsafe(MemoryMarshal.Cast<T, ushort>(value)), unsafe(MemoryMarshal.Cast<T, ushort>(destination)), unsafe(Unsafe.As<T, ushort>(ref separator)), unsafe(Unsafe.As<T, ushort>(ref quote)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryCopyPlainCore<T>(ReadOnlySpan<T> value, Span<T> destination, T separator, T quote)
        where T : unmanaged, IBinaryInteger<T>
    {
        if ((uint)destination.Length < (uint)value.Length)
            throw new ArgumentException("The destination is too short.", nameof(destination));

        ref var source = ref unsafe(MemoryMarshal.GetReference(value));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var length = value.Length;
        if (Vector128.IsHardwareAccelerated && length >= Vector128<T>.Count)
        {
            var separators = Vector128.Create(separator);
            var quotes = Vector128.Create(quote);
            var cr = Vector128.Create(T.CreateTruncating('\r'));
            var lf = Vector128.Create(T.CreateTruncating('\n'));

            // The last block overlaps the previous one when the length is not a multiple of the vector size
            var lastOffset = (nuint)(length - Vector128<T>.Count);
            nuint offset = 0;
            while (true)
            {
                var blockOffset = Math.Min(offset, lastOffset);
                var block = unsafe(Vector128.LoadUnsafe(ref source, blockOffset));
                if ((Vector128.Equals(block, separators) | Vector128.Equals(block, quotes) | Vector128.Equals(block, cr) | Vector128.Equals(block, lf)) != Vector128<T>.Zero)
                    return false;

                unsafe { block.StoreUnsafe(ref target, blockOffset); }
                if (offset >= lastOffset)
                    return true;

                offset += (nuint)Vector128<T>.Count;
            }
        }

        for (var i = 0; i < length; i++)
        {
            var c = unsafe(Unsafe.Add(ref source, i));
            if (c == separator || c == quote || c == T.CreateTruncating('\r') || c == T.CreateTruncating('\n'))
                return false;

            unsafe { Unsafe.Add(ref target, i) = c; }
        }

        return true;
    }

    /// <summary>Converts an ASCII value that has no separator, quote, CR or LF to UTF-8 in <paramref name="destination"/>.</summary>
    /// <returns><see langword="false"/> when the value is not ASCII or has one of these characters: the content of <paramref name="destination"/> is then undefined.</returns>
    /// <remarks>
    /// This is <see cref="TryCopyPlain{T}"/> for the UTF-8 writer of text: the value is checked while it is narrowed, instead of searched before
    /// <see cref="Ascii.FromUtf16"/>. <paramref name="destination"/> must have at least the length of <paramref name="value"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryCopyPlainAscii(ReadOnlySpan<char> value, Span<byte> destination, byte separator, byte quote)
    {
        if ((uint)destination.Length < (uint)value.Length)
            throw new ArgumentException("The destination is too short.", nameof(destination));

        ref var source = ref unsafe(Unsafe.As<char, ushort>(ref MemoryMarshal.GetReference(value)));
        ref var target = ref unsafe(MemoryMarshal.GetReference(destination));
        var length = value.Length;
        if (Vector128.IsHardwareAccelerated && length >= Vector128<byte>.Count)
        {
            var nonAscii = Vector128.Create((ushort)0xFF80);
            var separators = Vector128.Create(separator);
            var quotes = Vector128.Create(quote);
            var cr = Vector128.Create((byte)'\r');
            var lf = Vector128.Create((byte)'\n');

            // The last block overlaps the previous one when the length is not a multiple of the vector size
            var lastOffset = (nuint)(length - Vector128<byte>.Count);
            nuint offset = 0;
            while (true)
            {
                var blockOffset = Math.Min(offset, lastOffset);
                var lower = unsafe(Vector128.LoadUnsafe(ref source, blockOffset));
                var upper = unsafe(Vector128.LoadUnsafe(ref source, blockOffset + (nuint)Vector128<ushort>.Count));
                if (((lower | upper) & nonAscii) != Vector128<ushort>.Zero)
                    return false;

                var block = Vector128.Narrow(lower, upper);
                if ((Vector128.Equals(block, separators) | Vector128.Equals(block, quotes) | Vector128.Equals(block, cr) | Vector128.Equals(block, lf)) != Vector128<byte>.Zero)
                    return false;

                unsafe { block.StoreUnsafe(ref target, blockOffset); }
                if (offset >= lastOffset)
                    return true;

                offset += (nuint)Vector128<byte>.Count;
            }
        }

        for (var i = 0; i < length; i++)
        {
            var c = unsafe(Unsafe.Add(ref source, i));
            if (c > 0x7F || c == separator || c == quote || c == '\r' || c == '\n')
                return false;

            unsafe { Unsafe.Add(ref target, i) = (byte)c; }
        }

        return true;
    }

    // Narrows 16 UTF-16 code units, saturated to 0xFF
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector128<byte> NarrowArm64(ref ushort chars, nuint offset)
    {
        var lower = AdvSimd.ExtractNarrowingSaturateLower(unsafe(Vector128.LoadUnsafe(ref chars, offset)));
        return AdvSimd.ExtractNarrowingSaturateUpper(lower, unsafe(Vector128.LoadUnsafe(ref chars, offset + 8)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MatchArm64(Vector128<byte> value0, Vector128<byte> value1, Vector128<byte> value2, Vector128<byte> value3, byte separator, byte quote, out ulong separators, out ulong specials)
    {
        var separatorVector = Vector128.Create(separator);
        var quoteVector = Vector128.Create(quote);
        var cr = Vector128.Create((byte)'\r');
        var lf = Vector128.Create((byte)'\n');

        var separators0 = Vector128.Equals(value0, separatorVector);
        var separators1 = Vector128.Equals(value1, separatorVector);
        var separators2 = Vector128.Equals(value2, separatorVector);
        var separators3 = Vector128.Equals(value3, separatorVector);
        separators = ToMaskArm64(separators0, separators1, separators2, separators3);
        specials = ToMaskArm64(
            separators0 | Vector128.Equals(value0, quoteVector) | Vector128.Equals(value0, cr) | Vector128.Equals(value0, lf),
            separators1 | Vector128.Equals(value1, quoteVector) | Vector128.Equals(value1, cr) | Vector128.Equals(value1, lf),
            separators2 | Vector128.Equals(value2, quoteVector) | Vector128.Equals(value2, cr) | Vector128.Equals(value2, lf),
            separators3 | Vector128.Equals(value3, quoteVector) | Vector128.Equals(value3, cr) | Vector128.Equals(value3, lf));
    }

    // Arm64 has no movemask instruction: each element keeps the bit of its position in its byte, and three pairwise additions gather the bytes of
    // the four vectors into the 64-bit mask
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong ToMaskArm64(Vector128<byte> matches0, Vector128<byte> matches1, Vector128<byte> matches2, Vector128<byte> matches3)
    {
        var weights = Vector128.Create((byte)1, 2, 4, 8, 16, 32, 64, 128, 1, 2, 4, 8, 16, 32, 64, 128);
        var sum01 = AdvSimd.Arm64.AddPairwise(matches0 & weights, matches1 & weights);
        var sum23 = AdvSimd.Arm64.AddPairwise(matches2 & weights, matches3 & weights);
        var sum = AdvSimd.Arm64.AddPairwise(sum01, sum23);
        return AdvSimd.Arm64.AddPairwise(sum, sum).AsUInt64().ToScalar();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Match(Vector512<byte> value, byte separator, byte quote, out ulong separators, out ulong specials)
    {
        // The masks are combined after the extraction: combining the comparisons first is slower with AVX-512, where they are mask registers
        separators = Vector512.Equals(value, Vector512.Create(separator)).ExtractMostSignificantBits();
        var quotes = Vector512.Equals(value, Vector512.Create(quote)).ExtractMostSignificantBits();
        var lineTerminators = (Vector512.Equals(value, Vector512.Create((byte)'\r')) | Vector512.Equals(value, Vector512.Create((byte)'\n'))).ExtractMostSignificantBits();
        specials = separators | quotes | lineTerminators;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Match(Vector256<byte> value, byte separator, byte quote, out ulong separators, out ulong specials)
    {
        // The masks are combined after the extraction: combining the comparisons first is slower with AVX-512, where they are mask registers
        separators = Vector256.Equals(value, Vector256.Create(separator)).ExtractMostSignificantBits();
        var quotes = Vector256.Equals(value, Vector256.Create(quote)).ExtractMostSignificantBits();
        var lineTerminators = (Vector256.Equals(value, Vector256.Create((byte)'\r')) | Vector256.Equals(value, Vector256.Create((byte)'\n'))).ExtractMostSignificantBits();
        specials = separators | quotes | lineTerminators;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Match(Vector128<byte> value, byte separator, byte quote, out ulong separators, out ulong specials)
    {
        // The masks are combined after the extraction: combining the comparisons first is slower with AVX-512, where they are mask registers
        separators = Vector128.Equals(value, Vector128.Create(separator)).ExtractMostSignificantBits();
        var quotes = Vector128.Equals(value, Vector128.Create(quote)).ExtractMostSignificantBits();
        var lineTerminators = (Vector128.Equals(value, Vector128.Create((byte)'\r')) | Vector128.Equals(value, Vector128.Create((byte)'\n'))).ExtractMostSignificantBits();
        specials = separators | quotes | lineTerminators;
    }
}
