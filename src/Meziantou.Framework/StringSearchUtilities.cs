using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework;

public static partial class StringSearchUtilities
{
    /// <summary>Compute the <see href="http://en.wikipedia.org/wiki/Hamming_distance">Hamming distance</see>.</summary>
    /// <param name="word1"> The first word.</param>
    /// <param name="word2"> The second word.</param>
    /// <returns> The hamming distance.</returns>
    public static uint Hamming(uint word1, uint word2)
    {
        uint result = 0;
        while (word1 != 0 || word2 != 0)
        {
            var u = (word1 & 1) ^ (word2 & 1);
            result += u;
            word1 = (word1 >> 1) & 0x7FFFFFFF;
            word2 = (word2 >> 1) & 0x7FFFFFFF;
        }

        return result;
    }

    /// <summary>Compute the <see href="http://en.wikipedia.org/wiki/Hamming_distance">Hamming distance</see>.</summary>
    /// <param name="word1">The first word.</param>
    /// <param name="word2">The second word.</param>
    /// <exception cref="ArgumentException">Lists must have the same length.</exception>
    /// <returns> The hamming distance.</returns>
    public static int Hamming(string word1, string word2)
    {
        ArgumentNullException.ThrowIfNull(word1);
        ArgumentNullException.ThrowIfNull(word2);

        if (word1.Length != word2.Length)
            throw new ArgumentException("Strings must have the same length.", nameof(word2));

        var result = 0;
        for (var i = 0; i < word1.Length; i++)
        {
            if (word1[i] != word2[i])
            {
                result++;
            }
        }

        return result;
    }

    /// <summary>Compute the <see href="http://en.wikipedia.org/wiki/Hamming_distance">Hamming distance</see>.</summary>
    /// <typeparam name="T">Type of elements.</typeparam>
    /// <param name="word1">The first list.</param>
    /// <param name="word2">The second most.</param>
    /// <exception cref="ArgumentException">Lists must have the same length.</exception>
    /// <returns> The hamming distance.</returns>
    public static int Hamming<T>(IEnumerable<T> word1, IEnumerable<T> word2)
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(word1);
        ArgumentNullException.ThrowIfNull(word2);

        var result = 0;

        using var enumerator1 = word1.GetEnumerator();
        using var enumerator2 = word2.GetEnumerator();
        bool firstMoveNext;
        var secondMoveNext = false;

        while ((firstMoveNext = enumerator1.MoveNext()) && (secondMoveNext = enumerator2.MoveNext()))
        {
            if (!enumerator1.Current.Equals(enumerator2.Current))
            {
                result++;
            }
        }

        if (firstMoveNext != secondMoveNext)
            throw new ArgumentException("Lists must have the same length.", nameof(word2));

        return result;
    }

    /// <summary>Compute the <see href="http://en.wikipedia.org/wiki/Hamming_distance">Hamming distance</see>.</summary>
    /// <param name="word1"> The first word.</param>
    /// <param name="word2"> The second word.</param>
    /// <returns> The Levenshtein distance.</returns>
    public static int Levenshtein(string word1, string word2)
    {
        ArgumentNullException.ThrowIfNull(word1);
        ArgumentNullException.ThrowIfNull(word2);

        return Levenshtein(word1.AsSpan(), word2.AsSpan());
    }

    /// <summary>Compute the <see href="http://en.wikipedia.org/wiki/Levenshtein_distance">Levenshtein distance</see>.</summary>
    /// <param name="word1">The first word.</param>
    /// <param name="word2">The second word.</param>
    /// <returns>The Levenshtein distance.</returns>
    public static int Levenshtein(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        return LevenshteinCore(word1, word2, maxDistance: null);
    }

    /// <summary>Compute the <see href="http://en.wikipedia.org/wiki/Levenshtein_distance">Levenshtein distance</see> with a maximum distance threshold.</summary>
    /// <param name="word1">The first word.</param>
    /// <param name="word2">The second word.</param>
    /// <param name="maxDistance">Maximum accepted distance. If the computed distance is greater, this method returns <c>maxDistance + 1</c>.</param>
    /// <returns>The Levenshtein distance, or <c>maxDistance + 1</c> when the distance is greater than <paramref name="maxDistance"/>.</returns>
    public static int Levenshtein(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2, int maxDistance)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDistance);

        return LevenshteinCore(word1, word2, maxDistance);
    }

    /// <summary>Compute Levenshtein distance between one word and multiple candidates.</summary>
    /// <param name="word">Word used as reference.</param>
    /// <param name="candidates">Candidate words.</param>
    /// <param name="maxDegreeOfParallelism">Maximum number of worker threads, or <c>-1</c> for default.</param>
    /// <returns>An array containing one distance per candidate.</returns>
    public static int[] LevenshteinBatch(string word, IReadOnlyList<string> candidates, int maxDegreeOfParallelism = -1)
    {
        ArgumentNullException.ThrowIfNull(word);
        ArgumentNullException.ThrowIfNull(candidates);

        if (maxDegreeOfParallelism is 0 or < -1)
            throw new ArgumentOutOfRangeException(nameof(maxDegreeOfParallelism));

        for (var i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] is null)
                throw new ArgumentException("Candidates cannot contain null values.", nameof(candidates));
        }

        var results = new int[candidates.Count];
        if (results.Length is 0)
        {
            return results;
        }

        var parallelOptions = new ParallelOptions();
        if (maxDegreeOfParallelism > 0)
        {
            parallelOptions.MaxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        Parallel.For(0, candidates.Count, parallelOptions, i =>
        {
            results[i] = Levenshtein(word, candidates[i]);
        });

        return results;
    }

    private static int LevenshteinCore(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2, int? maxDistance)
    {
        if (word1.SequenceEqual(word2))
        {
            return 0;
        }

        var commonPrefixLength = CommonPrefixLength(word1, word2);
        word1 = word1[commonPrefixLength..];
        word2 = word2[commonPrefixLength..];

        var commonSuffixLength = CommonSuffixLength(word1, word2);
        if (commonSuffixLength > 0)
        {
            word1 = word1[..^commonSuffixLength];
            word2 = word2[..^commonSuffixLength];
        }

        if (word1.Length is 0)
        {
            return CapDistance(word2.Length, maxDistance);
        }

        if (word2.Length is 0)
        {
            return CapDistance(word1.Length, maxDistance);
        }

        if (word2.Length > word1.Length)
        {
            var temp = word1;
            word1 = word2;
            word2 = temp;
        }

        if (maxDistance is int boundedDistance && (word1.Length - word2.Length > boundedDistance))
        {
            return boundedDistance + 1;
        }

        if (word2.Length <= 64)
        {
            var distance = LevenshteinMyers64(word1, word2);
            if (maxDistance is int myersBoundedDistance && distance > myersBoundedDistance)
            {
                return myersBoundedDistance + 1;
            }

            return distance;
        }

        if (maxDistance is int maxDistanceValue)
        {
            return LevenshteinBanded(word1, word2, maxDistanceValue);
        }

        return LevenshteinPooled(word1, word2);
    }

    private static int CommonPrefixLength(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        var minLength = Math.Min(word1.Length, word2.Length);
        if (minLength is 0)
        {
            return 0;
        }

        var index = 0;
        if (Vector.IsHardwareAccelerated && minLength >= Vector<ushort>.Count)
        {
            var vectorLength = Vector<ushort>.Count;
            var values1 = MemoryMarshal.Cast<char, ushort>(word1);
            var values2 = MemoryMarshal.Cast<char, ushort>(word2);

            for (; index <= minLength - vectorLength; index += vectorLength)
            {
                var vector1 = new Vector<ushort>(values1.Slice(index, vectorLength));
                var vector2 = new Vector<ushort>(values2.Slice(index, vectorLength));
                if (!Vector.EqualsAll(vector1, vector2))
                {
                    break;
                }
            }
        }

        while (index < minLength && word1[index] == word2[index])
        {
            index++;
        }

        return index;
    }

    private static int CommonSuffixLength(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        var minLength = Math.Min(word1.Length, word2.Length);
        if (minLength is 0)
        {
            return 0;
        }

        var suffixLength = 0;
        while (suffixLength < minLength && word1[^(suffixLength + 1)] == word2[^(suffixLength + 1)])
        {
            suffixLength++;
        }

        return suffixLength;
    }

    private static int LevenshteinMyers64(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        if (word2.Length is 0)
            return word1.Length;

        Debug.Assert(word2.Length <= 64, "This method is only valid for word2 with length less than or equal to 64.");

        return IsAscii(word2)
            ? LevenshteinMyers64Ascii(word1, word2)
            : LevenshteinMyers64Unicode(word1, word2);
    }

    private static int LevenshteinMyers64Ascii(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        Span<ulong> asciiPatternMasks = stackalloc ulong[128];
        for (var i = 0; i < word2.Length; i++)
        {
            asciiPatternMasks[word2[i]] |= 1UL << i;
        }

        var score = word2.Length;
        var previousPositive = ulong.MaxValue;
        var previousNegative = 0UL;
        var topBitMask = 1UL << (word2.Length - 1);

        foreach (var c in word1)
        {
            var equalMask = c < 128 ? asciiPatternMasks[c] : 0UL;
            var xVector = equalMask | previousNegative;
            var xHorizontal = (((equalMask & previousPositive) + previousPositive) ^ previousPositive) | equalMask;
            var positiveHorizontal = previousNegative | ~(xHorizontal | previousPositive);
            var negativeHorizontal = previousPositive & xHorizontal;

            if ((positiveHorizontal & topBitMask) != 0)
            {
                score++;
            }
            else if ((negativeHorizontal & topBitMask) != 0)
            {
                score--;
            }

            positiveHorizontal = (positiveHorizontal << 1) | 1UL;
            negativeHorizontal <<= 1;

            previousPositive = negativeHorizontal | ~(xVector | positiveHorizontal);
            previousNegative = positiveHorizontal & xVector;
        }

        return score;
    }

    private static int LevenshteinMyers64Unicode(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        var patternMasks = new Dictionary<char, ulong>(capacity: word2.Length);
        for (var i = 0; i < word2.Length; i++)
        {
            if (!patternMasks.TryGetValue(word2[i], out var mask))
            {
                mask = 0;
            }

            patternMasks[word2[i]] = mask | (1UL << i);
        }

        var score = word2.Length;
        var previousPositive = ulong.MaxValue;
        var previousNegative = 0UL;
        var topBitMask = 1UL << (word2.Length - 1);

        foreach (var c in word1)
        {
            patternMasks.TryGetValue(c, out var equalMask);
            var xVector = equalMask | previousNegative;
            var xHorizontal = (((equalMask & previousPositive) + previousPositive) ^ previousPositive) | equalMask;
            var positiveHorizontal = previousNegative | ~(xHorizontal | previousPositive);
            var negativeHorizontal = previousPositive & xHorizontal;

            if ((positiveHorizontal & topBitMask) != 0)
            {
                score++;
            }
            else if ((negativeHorizontal & topBitMask) != 0)
            {
                score--;
            }

            positiveHorizontal = (positiveHorizontal << 1) | 1UL;
            negativeHorizontal <<= 1;

            previousPositive = negativeHorizontal | ~(xVector | positiveHorizontal);
            previousNegative = positiveHorizontal & xVector;
        }

        return score;
    }

    private static int LevenshteinPooled(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2)
    {
        const int StackAllocThreshold = 256;
        var rowLength = word2.Length + 1;
        if (rowLength <= StackAllocThreshold)
        {
            Span<int> row = stackalloc int[rowLength];
            return LevenshteinPooledCore(word1, word2, row);
        }

        var rentedRow = ArrayPool<int>.Shared.Rent(rowLength);
        try
        {
            return LevenshteinPooledCore(word1, word2, rentedRow.AsSpan(0, rowLength));
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedRow);
        }
    }

    private static int LevenshteinPooledCore(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2, Span<int> row)
    {
        for (var i = 0; i <= word2.Length; i++)
        {
            row[i] = i;
        }

        for (var i = 1; i <= word1.Length; i++)
        {
            var previousDiagonal = row[0];
            row[0] = i;

            for (var j = 1; j <= word2.Length; j++)
            {
                var previousAbove = row[j];
                var substitutionCost = word1[i - 1] == word2[j - 1] ? 0 : 1;
                var value = Math.Min(
                    Math.Min(row[j] + 1, row[j - 1] + 1),
                    previousDiagonal + substitutionCost);

                row[j] = value;
                previousDiagonal = previousAbove;
            }
        }

        return row[word2.Length];
    }

    private static int LevenshteinBanded(ReadOnlySpan<char> word1, ReadOnlySpan<char> word2, int maxDistance)
    {
        if (maxDistance is 0)
        {
            return word1.SequenceEqual(word2) ? 0 : 1;
        }

        if (word1.Length - word2.Length > maxDistance)
        {
            return maxDistance + 1;
        }

        const int StackAllocThreshold = 256;
        var rowLength = word2.Length + 1;

        if (rowLength <= StackAllocThreshold)
        {
            Span<int> row = stackalloc int[rowLength];
            return LevenshteinBandedCore(word1, word2, maxDistance, row);
        }

        var rentedRow = ArrayPool<int>.Shared.Rent(rowLength);
        try
        {
            return LevenshteinBandedCore(word1, word2, maxDistance, rentedRow.AsSpan(0, rowLength));
        }
        finally
        {
            ArrayPool<int>.Shared.Return(rentedRow);
        }

        static int LevenshteinBandedCore(ReadOnlySpan<char> left, ReadOnlySpan<char> right, int boundedDistance, Span<int> row)
        {
            const int InfiniteValue = int.MaxValue / 4;

            row[0] = 0;
            for (var j = 1; j <= right.Length; j++)
            {
                row[j] = j <= boundedDistance ? j : InfiniteValue;
            }

            for (var i = 1; i <= left.Length; i++)
            {
                var start = Math.Max(1, i - boundedDistance);
                var end = Math.Min(right.Length, i + boundedDistance);

                if (start > end)
                {
                    return boundedDistance + 1;
                }

                var previousDiagonal = row[0];
                row[0] = i <= boundedDistance ? i : InfiniteValue;

                if (start > 1)
                {
                    previousDiagonal = row[start - 1];
                    row[start - 1] = InfiniteValue;
                }

                var hasValuesInBand = false;
                for (var j = start; j <= end; j++)
                {
                    var previousAbove = row[j];
                    var substitutionCost = left[i - 1] == right[j - 1] ? 0 : 1;
                    var value = Math.Min(
                        Math.Min(previousAbove + 1, row[j - 1] + 1),
                        previousDiagonal + substitutionCost);

                    row[j] = value;
                    previousDiagonal = previousAbove;

                    if (value <= boundedDistance)
                    {
                        hasValuesInBand = true;
                    }
                }

                if (end < right.Length)
                {
                    row[end + 1] = InfiniteValue;
                }

                if (!hasValuesInBand)
                {
                    return boundedDistance + 1;
                }
            }

            var distance = row[right.Length];
            return distance <= boundedDistance ? distance : boundedDistance + 1;
        }
    }

    private static bool IsAscii(ReadOnlySpan<char> value)
    {
        const ushort NonAsciiMaskValue = 0xFF80;

        var values = MemoryMarshal.Cast<char, ushort>(value);
        var index = 0;
        ref var valuesReference = ref MemoryMarshal.GetReference(values);

        if (Vector512.IsHardwareAccelerated && values.Length >= Vector512<ushort>.Count)
        {
            var nonAsciiMask = Vector512.Create(NonAsciiMaskValue);
            var vectorLength = Vector512<ushort>.Count;

            for (; index <= values.Length - vectorLength; index += vectorLength)
            {
                var current = Vector512.LoadUnsafe(ref valuesReference, (nuint)index);
                if (!Vector512.EqualsAll(Vector512.BitwiseAnd(current, nonAsciiMask), Vector512<ushort>.Zero))
                {
                    return false;
                }
            }
        }

        if (Vector256.IsHardwareAccelerated && values.Length - index >= Vector256<ushort>.Count)
        {
            var nonAsciiMask = Vector256.Create(NonAsciiMaskValue);
            var vectorLength = Vector256<ushort>.Count;

            for (; index <= values.Length - vectorLength; index += vectorLength)
            {
                var current = Vector256.LoadUnsafe(ref valuesReference, (nuint)index);
                if (!Vector256.EqualsAll(Vector256.BitwiseAnd(current, nonAsciiMask), Vector256<ushort>.Zero))
                {
                    return false;
                }
            }
        }

        if (Vector128.IsHardwareAccelerated && values.Length - index >= Vector128<ushort>.Count)
        {
            var nonAsciiMask = Vector128.Create(NonAsciiMaskValue);
            var vectorLength = Vector128<ushort>.Count;

            for (; index <= values.Length - vectorLength; index += vectorLength)
            {
                var current = Vector128.LoadUnsafe(ref valuesReference, (nuint)index);
                if (!Vector128.EqualsAll(Vector128.BitwiseAnd(current, nonAsciiMask), Vector128<ushort>.Zero))
                {
                    return false;
                }
            }
        }

        for (; index < values.Length; index++)
        {
            if ((values[index] & NonAsciiMaskValue) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static int CapDistance(int distance, int? maxDistance)
    {
        if (maxDistance is int boundedDistance && distance > boundedDistance)
        {
            return boundedDistance + 1;
        }

        return distance;
    }
}
