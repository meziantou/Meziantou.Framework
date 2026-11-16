namespace Meziantou.Framework.Compression.Internals.BZip2;

/// <summary>
/// Sorts the suffixes of a text with the SA-IS algorithm (G. Nong, S. Zhang and W. H. Chan, "Two Efficient Algorithms for
/// Linear Time Suffix Array Construction", IEEE Transactions on Computers, 2011). The text ends with a sentinel, smaller
/// than every other symbol, that appears nowhere else.
/// </summary>
internal static class SuffixArray
{
    /// <summary>The symbols of a text: the sentinel is 0, the other symbols are in <c>[1, alphabetSize)</c>.</summary>
    internal interface IText
    {
        int this[int index] { get; }
    }

    /// <summary>A text of symbols stored in an array of 16-bit integers.</summary>
    internal readonly struct UInt16Text(ushort[] array) : IText
    {
        public int this[int index] => array[index];
    }

    /// <summary>A text of integers stored in an array, as the reduced texts of the recursion.</summary>
    internal readonly struct IntText(int[] array, int offset) : IText
    {
        public int this[int index] => array[offset + index];
    }

    /// <summary>
    /// Sorts the suffixes of <paramref name="text"/>, of <paramref name="length"/> symbols (the sentinel included), into
    /// <paramref name="sa"/>, of at least <paramref name="length"/> items: the first one is the sentinel.
    /// </summary>
    public static void Sort<TText>(TText text, int[] sa, int length, int alphabetSize)
        where TText : struct, IText
    {
        var types = new bool[length];
        SortCore(text, sa, length, alphabetSize, types);
    }

    // types[i] is true when the suffix i is an S-suffix (smaller than the suffix i + 1)
    private static void SortCore<TText>(TText text, int[] sa, int length, int alphabetSize, bool[] types)
        where TText : struct, IText
    {
        types[length - 1] = true;
        if (length > 1)
        {
            types[length - 2] = false;
        }

        for (var i = length - 3; i >= 0; i--)
        {
            var current = text[i];
            var next = text[i + 1];
            types[i] = current < next || (current == next && types[i + 1]);
        }

        var buckets = new int[alphabetSize];

        // Stage 1: sort the LMS substrings
        GetBuckets(text, length, buckets, end: true);
        Array.Fill(sa, -1, 0, length);
        for (var i = 1; i < length; i++)
        {
            if (IsLms(types, i))
            {
                sa[--buckets[text[i]]] = i;
            }
        }

        InduceL(text, sa, length, buckets, types);
        InduceS(text, sa, length, buckets, types);

        // Compact the sorted LMS substrings into the first items
        var lmsCount = 0;
        for (var i = 0; i < length; i++)
        {
            if (sa[i] > 0 && IsLms(types, sa[i]))
            {
                sa[lmsCount++] = sa[i];
            }
        }

        // Name the LMS substrings
        Array.Fill(sa, -1, lmsCount, length - lmsCount);
        var name = 0;
        var previous = -1;
        for (var i = 0; i < lmsCount; i++)
        {
            var position = sa[i];
            var different = false;
            for (var d = 0; d < length; d++)
            {
                if (previous is -1 || text[position + d] != text[previous + d] || types[position + d] != types[previous + d])
                {
                    different = true;
                    break;
                }

                if (d > 0 && (IsLms(types, position + d) || IsLms(types, previous + d)))
                    break;
            }

            if (different)
            {
                name++;
                previous = position;
            }

            sa[lmsCount + (position / 2)] = name - 1;
        }

        for (int i = length - 1, j = length - 1; i >= lmsCount; i--)
        {
            if (sa[i] >= 0)
            {
                sa[j--] = sa[i];
            }
        }

        // Stage 2: sort the reduced text, stored in the last items
        var reducedOffset = length - lmsCount;
        if (name < lmsCount)
        {
            SortCore(new IntText(sa, reducedOffset), sa, lmsCount, name, types);

            // The recursion overwrote the types: compute them again
            types[length - 1] = true;
            if (length > 1)
            {
                types[length - 2] = false;
            }

            for (var i = length - 3; i >= 0; i--)
            {
                var current = text[i];
                var next = text[i + 1];
                types[i] = current < next || (current == next && types[i + 1]);
            }
        }
        else
        {
            for (var i = 0; i < lmsCount; i++)
            {
                sa[sa[reducedOffset + i]] = i;
            }
        }

        // Stage 3: induce the suffix array from the sorted LMS suffixes
        for (int i = 1, j = 0; i < length; i++)
        {
            if (IsLms(types, i))
            {
                sa[reducedOffset + j++] = i;
            }
        }

        for (var i = 0; i < lmsCount; i++)
        {
            sa[i] = sa[reducedOffset + sa[i]];
        }

        Array.Fill(sa, -1, lmsCount, length - lmsCount);
        GetBuckets(text, length, buckets, end: true);
        for (var i = lmsCount - 1; i >= 0; i--)
        {
            var j = sa[i];
            sa[i] = -1;
            sa[--buckets[text[j]]] = j;
        }

        InduceL(text, sa, length, buckets, types);
        InduceS(text, sa, length, buckets, types);
    }

    private static bool IsLms(bool[] types, int index) => index > 0 && types[index] && !types[index - 1];

    private static void GetBuckets<TText>(TText text, int length, int[] buckets, bool end)
        where TText : struct, IText
    {
        Array.Clear(buckets);
        for (var i = 0; i < length; i++)
        {
            buckets[text[i]]++;
        }

        var sum = 0;
        for (var i = 0; i < buckets.Length; i++)
        {
            sum += buckets[i];
            buckets[i] = end ? sum : sum - buckets[i];
        }
    }

    private static void InduceL<TText>(TText text, int[] sa, int length, int[] buckets, bool[] types)
        where TText : struct, IText
    {
        GetBuckets(text, length, buckets, end: false);
        for (var i = 0; i < length; i++)
        {
            var j = sa[i] - 1;
            if (j >= 0 && !types[j])
            {
                sa[buckets[text[j]]++] = j;
            }
        }
    }

    private static void InduceS<TText>(TText text, int[] sa, int length, int[] buckets, bool[] types)
        where TText : struct, IText
    {
        GetBuckets(text, length, buckets, end: true);
        for (var i = length - 1; i >= 0; i--)
        {
            var j = sa[i] - 1;
            if (j >= 0 && types[j])
            {
                sa[--buckets[text[j]]] = j;
            }
        }
    }
}
