using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The language-specific hyphenation (https://drafts.csswg.org/css-text-4/#hyphens-property): "The UA must use the appropriate
// language-specific hyphenation character(s) and should apply any appropriate spelling changes just as for automatic hyphenation
// at the same point". The spelling changes of the WPT tests css/css-text/hyphens/i18n are applied at the soft hyphens and at the
// automatic hyphenation opportunities (no browser applies them): the Dutch diminutives of the words in -é (cafeetje is
// café-tje), the long double consonants of Hungarian (Összeg is Ösz-szeg), and the apostrophe that separates the syllables of
// Pinyin, which is removed at the break (tú’àn is tú-àn).
internal sealed partial class InlineLayoutAlgorithm
{
    private Dom.Element? _lastHyphenationLanguageElement;
    private HyphenationLanguage _lastHyphenationLanguage;

    /// <summary>The languages whose hyphenation has spelling changes or a hyphenation character of their own.</summary>
    private enum HyphenationLanguage : byte
    {
        Other,
        Dutch,
        Hungarian,
        Pinyin,
        Uyghur,
    }

    /// <summary>
    /// The text displayed at a hyphenation break before <c>_text[position]</c>: <see cref="RemovedBefore"/> characters before the
    /// break are replaced by <see cref="Hyphen"/> (the letters of the spelling change and the hyphenation character), and the next
    /// line starts <see cref="RemovedAfter"/> characters after the break.
    /// </summary>
    private readonly record struct HyphenationBreak(int RemovedBefore, string Hyphen, int RemovedAfter);

    /// <summary>The digraphs of Hungarian whose long form doubles their first letter (e.g. ssz is the long sz), longest first.</summary>
    private static readonly string[] HungarianDigraphs = ["dzs", "cs", "dz", "gy", "ly", "ny", "sz", "ty", "zs"];

    /// <summary>The apostrophes and the hyphens between the syllables of Pinyin (see <see cref="AddPinyinOpportunities"/>).</summary>
    private static readonly System.Buffers.SearchValues<char> PinyinSeparators = System.Buffers.SearchValues.Create("\u2019'\u2010-");

    /// <summary>The hyphenation break at a hyphenation opportunity of the text before <paramref name="position"/>, whose style is <paramref name="style"/>.</summary>
    private HyphenationBreak GetHyphenationBreak(int position, ComputedStyle style)
    {
        var text = _text.AsSpan(0, _length);
        var language = GetHyphenationLanguage(style);

        // (the break after a soft hyphen: the word ends before it)
        var end = position > 0 && text[position - 1] == SoftHyphen ? position - 1 : position;
        var before = end > 0 ? GetCodePointBefore(text[..end]) : 0;
        var hyphen = GetHyphen(style, before, language);
        switch (language)
        {
            case HyphenationLanguage.Dutch:
                // The diminutive of a word that ends with é is written with ee (café, cafeetje), which is é again at the break
                if (end >= 2 && position + 2 <= text.Length && text[end - 2] is 'e' or 'E' && text[end - 1] is 'e' or 'E'
                    && text[position] is 't' or 'T' && text[position + 1] is 'j' or 'J')
                {
                    return new HyphenationBreak(position - end + 2, (text[end - 1] == 'E' ? "\U000000C9" : "\U000000E9") + hyphen, 0);
                }

                break;

            case HyphenationLanguage.Hungarian:
                // A long double consonant written with a digraph is broken as two digraphs (ssz is sz-sz, ggy is gy-gy)
                if (end > 0)
                {
                    foreach (var digraph in HungarianDigraphs)
                    {
                        if (position + digraph.Length <= text.Length && text.Slice(position, digraph.Length).Equals(digraph, StringComparison.OrdinalIgnoreCase)
                            && char.ToLowerInvariant(text[end - 1]) == digraph[0])
                        {
                            return new HyphenationBreak(0, text.Slice(position + 1, digraph.Length - 1).ToString() + hyphen, 0);
                        }
                    }
                }

                break;

            case HyphenationLanguage.Pinyin:
                // The apostrophe that separates two syllables is not needed at the start of the line
                if (position < text.Length && IsPinyinApostrophe(text[position]))
                    return new HyphenationBreak(0, hyphen, 1);

                break;
        }

        return new HyphenationBreak(0, hyphen, 0);
    }

    /// <summary>The width of the hyphenation break at <paramref name="position"/>, in the text item <paramref name="index"/>: the hyphen, without the characters it replaces.</summary>
    private double MeasureHyphenationBreak(int index, int position, bool isFirstLine)
    {
        ref readonly var item = ref Items[index];
        var hyphenBreak = GetHyphenationBreak(position, item.Style);
        var width = TextShaper.MeasureWidth(hyphenBreak.Hyphen, item.Style.Fonts, GetHyphenShapingOptions(item.Style));
        if (hyphenBreak.RemovedBefore > 0 && position - hyphenBreak.RemovedBefore >= item.Start && position <= item.End)
        {
            var offsets = GetShaped(index, isFirstLine).Offsets;
            width -= offsets[position - item.Start] - offsets[position - hyphenBreak.RemovedBefore - item.Start];
        }

        return width;
    }

    /// <summary>
    /// The start of the content of a line: after the characters removed at the start of the line by the spelling change of the
    /// hyphenation break that ends the previous line (see <see cref="HyphenationBreak.RemovedAfter"/>).
    /// </summary>
    private LinePosition SkipHyphenationRemovedCharacters(LinePosition start)
    {
        if (!_opportunitiesComputed || start.Position <= 0 || start.Position >= _length || _opportunities[start.Position] is not (LineBreakOpportunity.Hyphen or LineBreakOpportunity.AutoHyphen))
            return start;

        // (the style of the text before the break)
        var index = start.Item;
        while (index > 0 && (Items[index].Kind != InlineItemKind.Text || Items[index].Start >= start.Position))
        {
            index--;
        }

        if (Items[index].Kind != InlineItemKind.Text)
            return start;

        var removed = GetHyphenationBreak(start.Position, Items[index].Style).RemovedAfter;
        if (removed == 0)
            return start;

        // The removed characters must be in the text item that starts the line
        var item = start.Item;
        while (item < Items.Length && Items[item].End <= start.Position)
        {
            item++;
        }

        return item < Items.Length && Items[item].Kind == InlineItemKind.Text && start.Position + removed <= Items[item].End ? start with { Position = start.Position + removed } : start;
    }

    /// <summary>
    /// Adds the hyphenation opportunities of Pinyin: an apostrophe between two letters separates two syllables, a hyphenation
    /// opportunity of the automatic hyphenation (<c>hyphens: auto</c>), or of a soft hyphen before it (a quotation mark for the
    /// rules of UAX #14, which forbid the break before it). A line broken at a hyphen between two syllables (U+2010 HYPHEN or
    /// U+002D HYPHEN-MINUS) repeats it at the start of the next line: the opportunity after the hyphen is a hyphenation
    /// opportunity before it (the hyphenation character ends the line, the hyphen starts the next one).
    /// </summary>
    private void AddPinyinOpportunities(Span<LineBreakOpportunity> opportunities)
    {
        var text = _text.AsSpan(0, _length);
        if (text.IndexOfAny(PinyinSeparators) < 0)
            return;

        foreach (ref readonly var item in Items)
        {
            if (item.Kind != InlineItemKind.Text || text[item.Start..item.End].IndexOfAny(PinyinSeparators) < 0)
                continue;

            var options = GetLineBreakOptions(item.Style);
            if (options.Hyphens == HyphensMode.None || GetHyphenationLanguage(item.Style) != HyphenationLanguage.Pinyin)
                continue;

            for (var i = Math.Max(item.Start, 1); i < item.End - 1; i++)
            {
                if (text[i] is (char)0x2010 or '-')
                {
                    // (a soft hyphen before the hyphen is already a hyphenation opportunity before it)
                    var syllableBefore = char.IsLetter(text[i - 1]) || (text[i - 1] == SoftHyphen && i >= 2 && char.IsLetter(text[i - 2]));
                    if (syllableBefore && char.IsLetter(text[i + 1]) && opportunities[i + 1] == LineBreakOpportunity.Allowed && opportunities[i] is LineBreakOpportunity.None or LineBreakOpportunity.Hyphen)
                    {
                        opportunities[i] = LineBreakOpportunity.Hyphen;
                        opportunities[i + 1] = LineBreakOpportunity.None;
                    }

                    continue;
                }

                if (!IsPinyinApostrophe(text[i]) || !char.IsLetter(text[i + 1]) || opportunities[i] is LineBreakOpportunity.Mandatory or LineBreakOpportunity.Allowed)
                    continue;

                if (text[i - 1] == SoftHyphen && i >= 2 && char.IsLetter(text[i - 2]))
                {
                    opportunities[i] = LineBreakOpportunity.Hyphen;
                }
                else if (options.Hyphens == HyphensMode.Auto && char.IsLetter(text[i - 1]))
                {
                    opportunities[i] = LineBreakOpportunity.AutoHyphen;
                }
            }
        }
    }

    private static bool IsPinyinApostrophe(char c) => c is (char)0x2019 or '\'';

    /// <summary>The language of the hyphenation of the text of a style (its content language).</summary>
    private HyphenationLanguage GetHyphenationLanguage(ComputedStyle style)
    {
        var element = style.Element ?? _block.Element;
        if (element is null)
            return HyphenationLanguage.Other;

        if (ReferenceEquals(element, _lastHyphenationLanguageElement))
            return _lastHyphenationLanguage;

        var language = HtmlElementStates.GetLanguage(element);
        var result = HyphenationLanguage.Other;
        if (!string.IsNullOrEmpty(language))
        {
            var subtags = language.Split('-');
            var primary = subtags[0];
            if (primary.Equals("nl", StringComparison.OrdinalIgnoreCase))
            {
                result = HyphenationLanguage.Dutch;
            }
            else if (primary.Equals("hu", StringComparison.OrdinalIgnoreCase))
            {
                result = HyphenationLanguage.Hungarian;
            }
            else if (primary.Equals("ug", StringComparison.OrdinalIgnoreCase))
            {
                result = HyphenationLanguage.Uyghur;
            }
            else if (primary.Equals("zh", StringComparison.OrdinalIgnoreCase) && subtags.Contains("pinyin", StringComparer.OrdinalIgnoreCase))
            {
                // The Pinyin romanization of Chinese (zh-Latn-pinyin)
                result = HyphenationLanguage.Pinyin;
            }
        }

        _lastHyphenationLanguageElement = element;
        _lastHyphenationLanguage = result;
        return result;
    }
}
