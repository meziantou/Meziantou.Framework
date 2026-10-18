using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// Extended grapheme cluster boundaries (https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundaries), the
/// typographic character units of CSS Text (https://drafts.csswg.org/css-text-4/#characters): line breaking with
/// <c>line-break: anywhere</c> and <c>overflow-wrap</c>, caret movement, <c>::first-letter</c>. The boundaries are the ones of
/// <see cref="Framework.Unicode.GetGraphemeClusterLength(ReadOnlySpan{char})"/>.
/// </summary>
internal static class GraphemeClusters
{
    /// <summary>Returns the end of the grapheme cluster that starts at <paramref name="index"/> (a cluster boundary).</summary>
    public static int GetNextBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= text.Length)
            return text.Length;

        return index + Framework.Unicode.GetGraphemeClusterLength(text[index..]);
    }

    /// <summary>Returns the start of the grapheme cluster that ends at or contains the position before <paramref name="index"/>.</summary>
    public static int GetPreviousBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, text.Length);
        if (index <= 0)
            return 0;

        // There is always a boundary after a control, CR or LF (GB4), except between CR and LF (GB3): segment forward from the
        // last one before the position, as the rules depend on the preceding context (GB9c, GB11, GB12, GB13)
        var start = index - 1;
        while (start > 0)
        {
            var c = text[start - 1];
            if ((c < 0x20 || c is (char)0x7F or (char)0x2028 or (char)0x2029) && !(c == '\r' && text[start] == '\n'))
                break;

            start--;
        }

        var previous = start;
        while (true)
        {
            var next = GetNextBoundary(text, previous);
            if (next >= index)
                return previous;

            previous = next;
        }
    }

    /// <summary>Returns the number of grapheme clusters of the text.</summary>
    public static int Count(ReadOnlySpan<char> text)
    {
        var count = 0;
        for (var i = 0; i < text.Length; i += Framework.Unicode.GetGraphemeClusterLength(text[i..]))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// Sets <c>boundaries[i]</c> to whether there is a grapheme cluster boundary before <c>text[i]</c> (never inside a surrogate
    /// pair). The boundary at the start of the text is included, the one at the end is not.
    /// </summary>
    public static void GetBoundaries(ReadOnlySpan<char> text, Span<bool> boundaries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(boundaries.Length, text.Length);
        boundaries[..text.Length].Clear();
        for (var i = 0; i < text.Length; i += Framework.Unicode.GetGraphemeClusterLength(text[i..]))
        {
            boundaries[i] = true;
        }
    }

    /// <summary>Reads the code point at <paramref name="index"/>; a lone surrogate is returned as is (its properties are the ones of the surrogate code point).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static int ReadCodePoint(ReadOnlySpan<char> text, int index, out int length)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            length = 2;
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        length = 1;
        return c;
    }
}
