using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
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
    public static int DefaultVectorSize { get; } =
        Vector512.IsHardwareAccelerated ? 64 :
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
    public static void GetMasks<T>(ref T start, int vectorSize, byte separator, byte quote, out ulong separators, out ulong quotes, out ulong lineTerminators)
        where T : unmanaged
    {
        if (typeof(T) == typeof(byte))
        {
            ref var bytes = ref unsafe(Unsafe.As<T, byte>(ref start));
            switch (vectorSize)
            {
                case 64:
                    Match(unsafe(Vector512.LoadUnsafe(ref bytes)), separator, quote, out separators, out quotes, out lineTerminators);
                    return;

                case 32:
                    Match(unsafe(Vector256.LoadUnsafe(ref bytes)), separator, quote, out separators, out quotes, out lineTerminators);
                    return;

                default:
                    Match(unsafe(Vector128.LoadUnsafe(ref bytes)), separator, quote, out separators, out quotes, out lineTerminators);
                    return;
            }
        }

        ref var chars = ref unsafe(Unsafe.As<T, ushort>(ref start));
        switch (vectorSize)
        {
            case 64:
                {
                    if (Avx512BW.IsSupported)
                    {
                        // The pack orders the 128-bit lanes of the two vectors alternately: the permutation restores the order of the elements
                        var packed = Avx512BW.PackUnsignedSaturate(unsafe(Vector512.LoadUnsafe(ref chars)).AsInt16(), unsafe(Vector512.LoadUnsafe(ref chars, 32)).AsInt16());
                        Match(Avx512F.PermuteVar8x64(packed.AsInt64(), Vector512.Create(0L, 2, 4, 6, 1, 3, 5, 7)).AsByte(), separator, quote, out separators, out quotes, out lineTerminators);
                        return;
                    }

                    var max = Vector512.Create((ushort)0xFF);
                    var value = Vector512.Narrow(Vector512.Min(unsafe(Vector512.LoadUnsafe(ref chars)), max), Vector512.Min(unsafe(Vector512.LoadUnsafe(ref chars, 32)), max));
                    Match(value, separator, quote, out separators, out quotes, out lineTerminators);
                    return;
                }

            case 32:
                {
                    if (Avx2.IsSupported)
                    {
                        var packed = Avx2.PackUnsignedSaturate(unsafe(Vector256.LoadUnsafe(ref chars)).AsInt16(), unsafe(Vector256.LoadUnsafe(ref chars, 16)).AsInt16());
                        Match(Avx2.Permute4x64(packed.AsInt64(), 0b11_01_10_00).AsByte(), separator, quote, out separators, out quotes, out lineTerminators);
                        return;
                    }

                    var max = Vector256.Create((ushort)0xFF);
                    var value = Vector256.Narrow(Vector256.Min(unsafe(Vector256.LoadUnsafe(ref chars)), max), Vector256.Min(unsafe(Vector256.LoadUnsafe(ref chars, 16)), max));
                    Match(value, separator, quote, out separators, out quotes, out lineTerminators);
                    return;
                }

            default:
                {
                    if (Sse2.IsSupported)
                    {
                        Match(Sse2.PackUnsignedSaturate(unsafe(Vector128.LoadUnsafe(ref chars)).AsInt16(), unsafe(Vector128.LoadUnsafe(ref chars, 8)).AsInt16()), separator, quote, out separators, out quotes, out lineTerminators);
                        return;
                    }

                    var max = Vector128.Create((ushort)0xFF);
                    var value = Vector128.Narrow(Vector128.Min(unsafe(Vector128.LoadUnsafe(ref chars)), max), Vector128.Min(unsafe(Vector128.LoadUnsafe(ref chars, 8)), max));
                    Match(value, separator, quote, out separators, out quotes, out lineTerminators);
                    return;
                }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Match(Vector512<byte> value, byte separator, byte quote, out ulong separators, out ulong quotes, out ulong lineTerminators)
    {
        separators = Vector512.Equals(value, Vector512.Create(separator)).ExtractMostSignificantBits();
        quotes = Vector512.Equals(value, Vector512.Create(quote)).ExtractMostSignificantBits();
        lineTerminators = (Vector512.Equals(value, Vector512.Create((byte)'\r')) | Vector512.Equals(value, Vector512.Create((byte)'\n'))).ExtractMostSignificantBits();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Match(Vector256<byte> value, byte separator, byte quote, out ulong separators, out ulong quotes, out ulong lineTerminators)
    {
        separators = Vector256.Equals(value, Vector256.Create(separator)).ExtractMostSignificantBits();
        quotes = Vector256.Equals(value, Vector256.Create(quote)).ExtractMostSignificantBits();
        lineTerminators = (Vector256.Equals(value, Vector256.Create((byte)'\r')) | Vector256.Equals(value, Vector256.Create((byte)'\n'))).ExtractMostSignificantBits();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Match(Vector128<byte> value, byte separator, byte quote, out ulong separators, out ulong quotes, out ulong lineTerminators)
    {
        separators = Vector128.Equals(value, Vector128.Create(separator)).ExtractMostSignificantBits();
        quotes = Vector128.Equals(value, Vector128.Create(quote)).ExtractMostSignificantBits();
        lineTerminators = (Vector128.Equals(value, Vector128.Create((byte)'\r')) | Vector128.Equals(value, Vector128.Create((byte)'\n'))).ExtractMostSignificantBits();
    }
}
