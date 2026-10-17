using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// Extended grapheme cluster boundaries (https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundaries), the
/// typographic character units of CSS Text (https://drafts.csswg.org/css-text-4/#characters): line breaking with
/// <c>line-break: anywhere</c> and <c>overflow-wrap</c>, caret movement, <c>::first-letter</c>.
/// </summary>
internal static class GraphemeClusters
{
    /// <summary>Returns the end of the grapheme cluster that starts at <paramref name="index"/> (a cluster boundary).</summary>
    public static int GetNextBoundary(ReadOnlySpan<char> text, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= text.Length)
            return text.Length;

        var breaker = default(GraphemeClusterBreaker);
        var codePoint = ReadCodePoint(text, index, out var length);
        breaker.IsBoundaryBefore(UnicodeProperties.GetSegmentationProperties(codePoint));
        index += length;
        while (index < text.Length)
        {
            codePoint = ReadCodePoint(text, index, out length);
            if (breaker.IsBoundaryBefore(UnicodeProperties.GetSegmentationProperties(codePoint)))
                return index;

            index += length;
        }

        return text.Length;
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
        var breaker = default(GraphemeClusterBreaker);
        for (var i = 0; i < text.Length;)
        {
            var codePoint = ReadCodePoint(text, i, out var length);
            if (breaker.IsBoundaryBefore(UnicodeProperties.GetSegmentationProperties(codePoint)))
            {
                count++;
            }

            i += length;
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
        var breaker = default(GraphemeClusterBreaker);
        for (var i = 0; i < text.Length;)
        {
            var codePoint = ReadCodePoint(text, i, out var length);
            boundaries[i] = breaker.IsBoundaryBefore(UnicodeProperties.GetSegmentationProperties(codePoint));
            if (length == 2)
            {
                boundaries[i + 1] = false;
            }

            i += length;
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

/// <summary>
/// The state of the extended grapheme cluster boundary rules (https://www.unicode.org/reports/tr29/#Grapheme_Cluster_Boundary_Rules)
/// while the code points of a text are read in order.
/// </summary>
internal struct GraphemeClusterBreaker
{
    private const byte EmojiNone = 0;
    private const byte EmojiExtend = 1; // \p{Extended_Pictographic} Extend*
    private const byte EmojiZwj = 2;    // \p{Extended_Pictographic} Extend* ZWJ

    private bool _started;
    private GraphemeClusterBreak _previous;
    private bool _oddRegionalIndicators;
    private byte _emojiState;
    private bool _afterLinker; // \p{InCB=Linker} \p{InCB=Extend}*

    /// <summary>
    /// Returns whether there is a boundary before the next code point, given its packed segmentation properties
    /// (<see cref="UnicodeProperties.GetSegmentationProperties"/>), and moves after it.
    /// </summary>
    public bool IsBoundaryBefore(byte properties)
    {
        var next = (GraphemeClusterBreak)(properties & 0xF);
        var incb = (IndicConjunctBreak)((properties >> 4) & 0x3);
        var extendedPictographic = (properties & 0x40) != 0;
        var boundary = !_started || IsBoundary(_previous, next, incb, extendedPictographic);

        _started = true;
        _oddRegionalIndicators = next == GraphemeClusterBreak.RegionalIndicator && !_oddRegionalIndicators;
        _emojiState = extendedPictographic ? EmojiExtend
            : _emojiState == EmojiExtend && next == GraphemeClusterBreak.Extend ? EmojiExtend
            : _emojiState == EmojiExtend && next == GraphemeClusterBreak.ZWJ ? EmojiZwj
            : EmojiNone;
        _afterLinker = incb == IndicConjunctBreak.Linker || (incb == IndicConjunctBreak.Extend && _afterLinker);
        _previous = next;
        return boundary;
    }

    private readonly bool IsBoundary(GraphemeClusterBreak previous, GraphemeClusterBreak next, IndicConjunctBreak nextIncb, bool nextExtendedPictographic)
    {
        // GB3
        if (previous == GraphemeClusterBreak.CR && next == GraphemeClusterBreak.LF)
            return false;

        // GB4, GB5
        if (previous is GraphemeClusterBreak.Control or GraphemeClusterBreak.CR or GraphemeClusterBreak.LF || next is GraphemeClusterBreak.Control or GraphemeClusterBreak.CR or GraphemeClusterBreak.LF)
            return true;

        // GB6, GB7, GB8
        if (previous == GraphemeClusterBreak.L && next is GraphemeClusterBreak.L or GraphemeClusterBreak.V or GraphemeClusterBreak.LV or GraphemeClusterBreak.LVT)
            return false;

        if (previous is GraphemeClusterBreak.LV or GraphemeClusterBreak.V && next is GraphemeClusterBreak.V or GraphemeClusterBreak.T)
            return false;

        if (previous is GraphemeClusterBreak.LVT or GraphemeClusterBreak.T && next == GraphemeClusterBreak.T)
            return false;

        // GB9, GB9a, GB9b
        if (next is GraphemeClusterBreak.Extend or GraphemeClusterBreak.ZWJ or GraphemeClusterBreak.SpacingMark || previous == GraphemeClusterBreak.Prepend)
            return false;

        // GB9c
        if (_afterLinker && nextIncb == IndicConjunctBreak.Consonant)
            return false;

        // GB11
        if (_emojiState == EmojiZwj && nextExtendedPictographic)
            return false;

        // GB12, GB13
        if (_oddRegionalIndicators && next == GraphemeClusterBreak.RegionalIndicator)
            return false;

        // GB999
        return true;
    }
}
