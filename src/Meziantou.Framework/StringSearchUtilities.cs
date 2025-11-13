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

        if (word1.Length is 0)
        {
            return word2.Length;
        }

        if (word2.Length is 0)
        {
            return word1.Length;
        }

        var lastColumn = new List<int>(word2.Length);
        for (var i = 1; i <= word2.Length; i++)
        {
            lastColumn.Add(i);
        }

        var lastValue = 0;
        for (var j = 1; j <= word1.Length; j++)
        {
            for (var i = 0; i < word2.Length; i++)
            {
                var x = (i is 0 ? j : lastValue) + 1;
                var y = lastColumn[i] + 1;
                var z = (i is 0 ? j - 1 : lastColumn[i - 1]) + (word1[j - 1] == word2[i] ? 0 : 1);

                var forLastValue = lastValue;
                lastValue = Math.Min(Math.Min(x, y), z);
                if (i > 0)
                {
                    lastColumn[i - 1] = forLastValue;
                }

                if (i == word2.Length - 1)
                {
                    lastColumn[i] = lastValue;
                }
            }
        }

        return lastValue;
    }
}
