using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Finds where the next run of equal elements starts, for the run-length encoders (TGA, BMP, OpenEXR and TIFF PackBits):
/// the literal elements before it are copied as they are. With hardware vectors, 16 bytes (<see cref="Vector128"/>) or 32
/// bytes (<see cref="Vector256"/>) are compared at a time; each method returns the result of its <c>...Scalar</c>
/// reference.
/// </summary>
internal static class RunLengthSearch
{
    /// <summary>Finds the first byte equal to the byte after it.</summary>
    /// <returns>Its position, or -1.</returns>
    public static int IndexOfPair(ReadOnlySpan<byte> values) => IndexOfRepeat(values, distance: 1, count: 2);

    /// <summary>The byte-at-a-time reference of <see cref="IndexOfPair(ReadOnlySpan{byte})"/>.</summary>
    internal static int IndexOfPairScalar(ReadOnlySpan<byte> values)
    {
        for (var i = 0; i + 1 < values.Length; i++)
        {
            if (values[i] == values[i + 1])
                return i;
        }

        return -1;
    }

    /// <summary>Finds the first byte equal to the two bytes after it.</summary>
    /// <returns>Its position, or -1.</returns>
    public static int IndexOfTriple(ReadOnlySpan<byte> values) => IndexOfRepeat(values, distance: 1, count: 3);

    /// <summary>The byte-at-a-time reference of <see cref="IndexOfTriple"/>.</summary>
    internal static int IndexOfTripleScalar(ReadOnlySpan<byte> values)
    {
        for (var i = 0; i + 2 < values.Length; i++)
        {
            if (values[i] == values[i + 1] && values[i] == values[i + 2])
                return i;
        }

        return -1;
    }

    /// <summary>Finds the first byte equal to the byte two positions after it.</summary>
    /// <returns>Its position, or -1.</returns>
    public static int IndexOfPairAtDistance2(ReadOnlySpan<byte> values) => IndexOfRepeat(values, distance: 2, count: 2);

    /// <summary>The byte-at-a-time reference of <see cref="IndexOfPairAtDistance2"/>.</summary>
    internal static int IndexOfPairAtDistance2Scalar(ReadOnlySpan<byte> values)
    {
        for (var i = 0; i + 2 < values.Length; i++)
        {
            if (values[i] == values[i + 2])
                return i;
        }

        return -1;
    }

    /// <summary>Finds the first 4-byte element equal to the element after it.</summary>
    /// <returns>Its position, or -1.</returns>
    public static int IndexOfPair(ReadOnlySpan<uint> values)
    {
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(values));
            if (Vector256.IsHardwareAccelerated)
            {
                for (; i <= values.Length - Vector256<uint>.Count - 1; i += Vector256<uint>.Count)
                {
                    var equal = Vector256.Equals(unsafe(Vector256.LoadUnsafe(ref source, (nuint)i)), unsafe(Vector256.LoadUnsafe(ref source, (nuint)i + 1)));
                    if (equal != Vector256<uint>.Zero)
                        return i + BitOperations.TrailingZeroCount(equal.ExtractMostSignificantBits());
                }
            }

            for (; i <= values.Length - Vector128<uint>.Count - 1; i += Vector128<uint>.Count)
            {
                var equal = Vector128.Equals(unsafe(Vector128.LoadUnsafe(ref source, (nuint)i)), unsafe(Vector128.LoadUnsafe(ref source, (nuint)i + 1)));
                if (equal != Vector128<uint>.Zero)
                    return i + BitOperations.TrailingZeroCount(equal.ExtractMostSignificantBits());
            }
        }

        var rest = IndexOfPairScalar(values[i..]);
        return rest < 0 ? -1 : i + rest;
    }

    /// <summary>The element-at-a-time reference of <see cref="IndexOfPair(ReadOnlySpan{uint})"/>.</summary>
    internal static int IndexOfPairScalar(ReadOnlySpan<uint> values)
    {
        for (var i = 0; i + 1 < values.Length; i++)
        {
            if (values[i] == values[i + 1])
                return i;
        }

        return -1;
    }

    /// <summary>Finds the first 3-byte element equal to the element after it.</summary>
    /// <param name="values">The elements, 3 bytes each.</param>
    /// <returns>The index of the element, or -1.</returns>
    public static int IndexOfPair24(ReadOnlySpan<byte> values)
    {
        // The bytes are compared with the bytes 3 positions further: an element equals the next one when the comparisons of
        // its 3 bytes are true, which a vector of 16 (32) bytes tells for its first 5 (10) elements
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(values));
            if (Vector256.IsHardwareAccelerated)
            {
                for (; i <= values.Length - Vector256<byte>.Count - 3; i += 30)
                {
                    var mask = Vector256.Equals(unsafe(Vector256.LoadUnsafe(ref source, (nuint)i)), unsafe(Vector256.LoadUnsafe(ref source, (nuint)i + 3))).ExtractMostSignificantBits();
                    var match = mask & (mask >> 1) & (mask >> 2) & 0x0924_9249u;
                    if (match != 0)
                        return (i + BitOperations.TrailingZeroCount(match)) / 3;
                }
            }

            for (; i <= values.Length - Vector128<byte>.Count - 3; i += 15)
            {
                var mask = Vector128.Equals(unsafe(Vector128.LoadUnsafe(ref source, (nuint)i)), unsafe(Vector128.LoadUnsafe(ref source, (nuint)i + 3))).ExtractMostSignificantBits();
                var match = mask & (mask >> 1) & (mask >> 2) & 0x1249u;
                if (match != 0)
                    return (i + BitOperations.TrailingZeroCount(match)) / 3;
            }
        }

        var rest = IndexOfPair24Scalar(values[i..]);
        return rest < 0 ? -1 : (i / 3) + rest;
    }

    /// <summary>The element-at-a-time reference of <see cref="IndexOfPair24"/>.</summary>
    /// <param name="values">The elements, 3 bytes each.</param>
    internal static int IndexOfPair24Scalar(ReadOnlySpan<byte> values)
    {
        for (var i = 0; (3 * i) + 5 < values.Length; i++)
        {
            if (values[3 * i] == values[(3 * i) + 3] && values[(3 * i) + 1] == values[(3 * i) + 4] && values[(3 * i) + 2] == values[(3 * i) + 5])
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Gets the number of leading elements equal to the first one, at most <paramref name="maximum"/> (the first one
    /// counted).
    /// </summary>
    /// <param name="values">At least one element.</param>
    /// <param name="maximum">The largest count, at least 1.</param>
    public static int CountRun<T>(ReadOnlySpan<T> values, int maximum)
        where T : IEquatable<T>
    {
        var limit = Math.Min(values.Length, maximum);
        if (limit < 2 || !values[1].Equals(values[0]))
            return 1;

        var other = values[2..limit].IndexOfAnyExcept(values[0]);
        return other < 0 ? limit : other + 2;
    }

    /// <summary>
    /// Finds the first position <c>i</c> whose byte equals the bytes at <c>i + distance</c> and, for 3 equal bytes, at
    /// <c>i + 2</c> (distance 1 only).
    /// </summary>
    private static int IndexOfRepeat(ReadOnlySpan<byte> values, int distance, int count)
    {
        var reach = count == 3 ? 2 : distance;
        var i = 0;
        if (Vector128.IsHardwareAccelerated)
        {
            ref var source = ref unsafe(MemoryMarshal.GetReference(values));
            if (Vector256.IsHardwareAccelerated)
            {
                for (; i <= values.Length - Vector256<byte>.Count - reach; i += Vector256<byte>.Count)
                {
                    var first = unsafe(Vector256.LoadUnsafe(ref source, (nuint)i));
                    var equal = Vector256.Equals(first, unsafe(Vector256.LoadUnsafe(ref source, (nuint)(i + distance))));
                    if (count == 3)
                    {
                        equal &= Vector256.Equals(first, unsafe(Vector256.LoadUnsafe(ref source, (nuint)i + 2)));
                    }

                    if (equal != Vector256<byte>.Zero)
                        return i + BitOperations.TrailingZeroCount(equal.ExtractMostSignificantBits());
                }
            }

            for (; i <= values.Length - Vector128<byte>.Count - reach; i += Vector128<byte>.Count)
            {
                var first = unsafe(Vector128.LoadUnsafe(ref source, (nuint)i));
                var equal = Vector128.Equals(first, unsafe(Vector128.LoadUnsafe(ref source, (nuint)(i + distance))));
                if (count == 3)
                {
                    equal &= Vector128.Equals(first, unsafe(Vector128.LoadUnsafe(ref source, (nuint)i + 2)));
                }

                if (equal != Vector128<byte>.Zero)
                    return i + BitOperations.TrailingZeroCount(equal.ExtractMostSignificantBits());
            }
        }

        var tail = values[i..];
        var rest = (distance, count) switch
        {
            (1, 2) => IndexOfPairScalar(tail),
            (1, _) => IndexOfTripleScalar(tail),
            _ => IndexOfPairAtDistance2Scalar(tail),
        };

        return rest < 0 ? -1 : i + rest;
    }
}
