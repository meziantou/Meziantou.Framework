using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The automatic hyphenation of hyphens: auto (https://drafts.csswg.org/css-text-4/#hyphenation): the hyphenation opportunities
// found by the dictionary of the language of the text (Hyphenator), added to the soft wrap opportunities of UAX #14.
internal sealed partial class InlineLayoutAlgorithm
{
    private const char SoftHyphen = (char)0x00AD;

    /// <summary>
    /// The start of the last word of the content when it has automatic hyphenation opportunities and is not the only word of
    /// the content, else -1: as in Chrome, it is only hyphenated when it starts the line (see <see cref="IsAutomaticHyphenationAllowed"/>).
    /// </summary>
    private int _lastHyphenatedWordStart = -1;

    private bool[] _hyphenationPoints = [];
    private Dom.Element? _lastHyphenatorElement;
    private string? _lastHyphenatorLanguage;
    private Hyphenator? _lastHyphenator;

    /// <summary>
    /// Adds the automatic hyphenation opportunities of the words whose style has <c>hyphens: auto</c>
    /// (https://drafts.csswg.org/css-text-4/#valdef-hyphens-auto): "Words may be broken at hyphenation opportunities determined
    /// automatically by an appropriate language-specific hyphenation resource in addition to those indicated explicitly by a
    /// conditional hyphen". A word is the text between two soft wrap opportunities (as in Chrome), the spaces after it excluded;
    /// it is hyphenated with the dictionary and the <c>hyphenate-limit-chars</c> of the style of its first character, and an
    /// opportunity is kept when the character before it has <c>hyphens: auto</c>.
    /// </summary>
    private void AddAutomaticHyphenationOpportunities(Span<LineBreakOpportunity> opportunities)
    {
        _lastHyphenatedWordStart = -1;
        var text = _text.AsSpan(0, _length);
        var items = Items;
        var itemIndex = 0;
        var wordStart = 0;
        for (var end = 1; end <= text.Length; end++)
        {
            if (end < text.Length && opportunities[end] is not (LineBreakOpportunity.Allowed or LineBreakOpportunity.Mandatory))
                continue;

            var wordEnd = end;
            while (wordEnd > wordStart && IsWordSeparator(text[wordEnd - 1]))
            {
                wordEnd--;
            }

            // The shortest word that the dictionaries hyphenate has 5 characters
            if (wordEnd - wordStart >= 5 && HyphenateWord(items, ref itemIndex, wordStart, wordEnd, opportunities)
                && IsAllWordSeparators(text[wordEnd..]) && !IsAllWordSeparators(text[..wordStart]))
            {
                _lastHyphenatedWordStart = wordStart;
            }

            wordStart = end;
        }

        // Chrome's IsBreakableSpace: the spaces, the tabs, the line feeds and the other space separators
        static bool IsWordSeparator(char c) => c == '\n' || IsSpace(c);

        static bool IsAllWordSeparators(ReadOnlySpan<char> text)
        {
            foreach (var c in text)
            {
                if (!IsWordSeparator(c))
                    return false;
            }

            return true;
        }
    }

    /// <summary>Adds the automatic hyphenation opportunities of the word from <paramref name="start"/> to <paramref name="end"/>, and returns whether it has one.</summary>
    private bool HyphenateWord(Span<InlineItem> items, ref int itemIndex, int start, int end, Span<LineBreakOpportunity> opportunities)
    {
        // The text item of the first character of the word
        while (itemIndex < items.Length && (items[itemIndex].End <= start || !items[itemIndex].HasText))
        {
            itemIndex++;
        }

        if (itemIndex >= items.Length || items[itemIndex].Kind != InlineItemKind.Text || !HasAutomaticHyphenation(items[itemIndex].Style))
            return false;

        // "Automatic hyphenation opportunities within a word must be ignored if the word contains a conditional hyphen (&shy; or
        // U+00AD SOFT HYPHEN), in favor of the conditional hyphen(s)"
        var text = _text.AsSpan(start, end - start);
        if (text.Contains(SoftHyphen))
            return false;

        var style = items[itemIndex].Style;
        if (GetHyphenator(style) is not { } hyphenator)
            return false;

        if (_hyphenationPoints.Length < text.Length)
        {
            _hyphenationPoints = new bool[Math.Max(text.Length, _hyphenationPoints.Length * 2)];
        }

        var points = _hyphenationPoints.AsSpan(0, text.Length);
        points.Clear();
        var limits = style.Layout.Text.HyphenateLimitChars;
        if (!hyphenator.FindOpportunities(text, limits.Word, limits.Before, limits.After, points))
            return false;

        var found = false;
        var index = itemIndex;
        for (var i = 1; i < points.Length; i++)
        {
            if (!points[i])
                continue;

            // The item of the character before the opportunity (the word may span several inline boxes)
            var position = start + i;
            while (index < items.Length && (items[index].End < position || !items[index].HasText))
            {
                index++;
            }

            if (index >= items.Length || items[index].Kind != InlineItemKind.Text || !HasAutomaticHyphenation(items[index].Style))
                continue;

            if (opportunities[position] is LineBreakOpportunity.None or LineBreakOpportunity.Emergency)
            {
                opportunities[position] = LineBreakOpportunity.AutoHyphen;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Whether the text of a style has <c>hyphens: auto</c> (with <c>word-break: auto-phrase</c>, the hyphenation opportunities are
    /// only taken when no other opportunity fits, see <see cref="BreakLine"/>).
    /// </summary>
    private bool HasAutomaticHyphenation(ComputedStyle style) => GetLineBreakOptions(style).Hyphens == HyphensMode.Auto;

    /// <summary>
    /// The hyphenator of the language of the text of a style (https://drafts.csswg.org/css-text-4/#hyphenation: "the UA must use
    /// the content language to determine the hyphenation rules"), or null when the language is unknown or has no dictionary (the
    /// text is not hyphenated automatically, as in Chrome).
    /// </summary>
    private Hyphenator? GetHyphenator(ComputedStyle style)
    {
        var element = style.Element ?? _block.Element;
        if (element is null)
            return null;

        if (ReferenceEquals(element, _lastHyphenatorElement))
            return _lastHyphenator;

        var language = HtmlElementStates.GetLanguage(element);
        if (_lastHyphenatorElement is null || !string.Equals(language, _lastHyphenatorLanguage, StringComparison.Ordinal))
        {
            _lastHyphenator = Hyphenator.Get(language);
            _lastHyphenatorLanguage = language;
        }

        _lastHyphenatorElement = element;
        return _lastHyphenator;
    }

    /// <summary>
    /// Whether the line that starts at <paramref name="lineStart"/> may break at the automatic hyphenation opportunity at
    /// <paramref name="position"/>. As in Chrome (<c>ShouldHyphenate</c> of <c>shaping_line_breaker.cc</c>), the last word of the
    /// content is not hyphenated, unless it is the only word or it starts the line (it would overflow); the min-content size
    /// uses every opportunity.
    /// </summary>
    private bool IsAutomaticHyphenationAllowed(int position, int lineStart) => _lastHyphenatedWordStart < 0 || position < _lastHyphenatedWordStart || _lastHyphenatedWordStart <= lineStart;

    private void ResetHyphenation()
    {
        _lastHyphenatedWordStart = -1;
        _lastHyphenatorElement = null;
        _lastHyphenatorLanguage = null;
        _lastHyphenator = null;
        _lastHyphenationLanguageElement = null;
    }
}
