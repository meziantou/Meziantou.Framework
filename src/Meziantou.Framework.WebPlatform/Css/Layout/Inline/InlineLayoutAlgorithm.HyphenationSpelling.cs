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
// Pinyin, which is removed at the break (tú’àn is tú-àn). The typographic conventions of other languages are applied too: the
// geminate l of Catalan (col·lecció is col-lecció), the ck of the 1901 German orthography (Zucker is Zuk-ker), and the hyphen of a
// compound word repeated at the start of the next line (see AddLanguageHyphenationOpportunities).
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
        Catalan,
        German1901,

        /// <summary>The languages whose typographic conventions repeat the hyphen of a compound word broken at its hyphen at the start of the next line.</summary>
        RepeatedHyphen,
    }

    /// <summary>
    /// The primary language subtags of the languages whose typographic conventions repeat the hyphen of a compound word broken at
    /// its hyphen at the start of the next line: Bosnian, Croatian, Czech, Lower and Upper Sorbian, Polish, Portuguese, Serbian,
    /// Slovak, Slovenian, and Spanish (Ortografía de la lengua española, 2010).
    /// </summary>
    private static readonly string[] RepeatedHyphenLanguages = ["bs", "cs", "dsb", "es", "hr", "hsb", "pl", "pt", "sh", "sk", "sl", "sr"];

    /// <summary>
    /// The text displayed at a hyphenation break before <c>_text[position]</c>: <see cref="RemovedBefore"/> characters before the
    /// break are replaced by <see cref="Hyphen"/> (the letters of the spelling change and the hyphenation character), and the next
    /// line starts <see cref="RemovedAfter"/> characters after the break.
    /// </summary>
    private readonly record struct HyphenationBreak(int RemovedBefore, string Hyphen, int RemovedAfter);

    /// <summary>The digraphs of Hungarian whose long form doubles their first letter (e.g. ssz is the long sz), longest first.</summary>
    private static readonly string[] HungarianDigraphs = ["dzs", "cs", "dz", "gy", "ly", "ny", "sz", "ty", "zs"];

    /// <summary>
    /// The characters of the hyphenation opportunities of <see cref="AddLanguageHyphenationOpportunities"/>: the apostrophes and
    /// the hyphens, the middle dots and the precomposed l with middle dot of Catalan.
    /// </summary>
    private static readonly System.Buffers.SearchValues<char> LanguageHyphenationCharacters = System.Buffers.SearchValues.Create("\u2019'\u2010-\u00b7\u2027\u0140\u013f");

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

            case HyphenationLanguage.Catalan:
                // The geminate l (l·l, or ŀl with the precomposed letter) is broken as l-l: the hyphen replaces the middle dot
                if (end > 0 && position < text.Length && text[position] is 'l' or 'L')
                {
                    if (end >= 2 && IsCatalanMiddleDot(text[end - 1]) && text[end - 2] is 'l' or 'L')
                        return new HyphenationBreak(position - end + 1, hyphen, 0);

                    if (text[end - 1] is (char)0x0140 or (char)0x013F)
                        return new HyphenationBreak(position - end + 1, (text[end - 1] == (char)0x013F ? "L" : "l") + hyphen, 0);
                }

                break;

            case HyphenationLanguage.German1901:
                // The ck is broken as k-k (Zucker is Zuk-ker)
                if (end > 0 && position < text.Length && text[end - 1] is 'c' or 'C' && text[position] is 'k' or 'K')
                    return new HyphenationBreak(position - end + 1, (text[end - 1] == 'C' ? "K" : "k") + hyphen, 0);

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
    /// Adds the hyphenation opportunities of the typographic conventions of the languages:
    /// <list type="bullet">
    /// <item>Pinyin: an apostrophe between two letters separates two syllables, a hyphenation opportunity of the automatic
    /// hyphenation (<c>hyphens: auto</c>), or of a soft hyphen before it (a quotation mark for the rules of UAX #14, which forbid
    /// the break before it).</item>
    /// <item>Catalan: the middle dot of a geminate l (l·l, or ŀl) is a hyphenation opportunity of the automatic hyphenation
    /// (the syllables are separated there, see <see cref="GetHyphenationBreak"/>).</item>
    /// <item>A line broken at a hyphen between two letters (U+2010 HYPHEN or U+002D HYPHEN-MINUS) repeats it at the start of the
    /// next line, between two syllables of Pinyin (unless <c>hyphens: none</c>), and in a compound word of the languages that
    /// repeat the hyphen (<see cref="RepeatedHyphenLanguages"/>) with <c>hyphens: auto</c> or a soft hyphen before the hyphen (so
    /// that the default <c>hyphens: manual</c> breaks as the browsers do): the opportunity after the hyphen is a hyphenation
    /// opportunity before it (the hyphenation character ends the line, the hyphen starts the next one).</item>
    /// </list>
    /// </summary>
    private void AddLanguageHyphenationOpportunities(Span<LineBreakOpportunity> opportunities)
    {
        var text = _text.AsSpan(0, _length);
        if (text.IndexOfAny(LanguageHyphenationCharacters) < 0)
            return;

        foreach (ref readonly var item in Items)
        {
            if (item.Kind != InlineItemKind.Text || text[item.Start..item.End].IndexOfAny(LanguageHyphenationCharacters) < 0)
                continue;

            var options = GetLineBreakOptions(item.Style);
            if (options.Hyphens == HyphensMode.None)
                continue;

            var language = GetHyphenationLanguage(item.Style);
            if (language is not (HyphenationLanguage.Pinyin or HyphenationLanguage.RepeatedHyphen or HyphenationLanguage.Catalan))
                continue;

            for (var i = Math.Max(item.Start, 1); i < item.End - 1; i++)
            {
                if (language == HyphenationLanguage.Catalan)
                {
                    // (the opportunity is after the middle dot, before the second l)
                    if (options.Hyphens == HyphensMode.Auto && text[i + 1] is 'l' or 'L' && opportunities[i + 1] == LineBreakOpportunity.None
                        && ((IsCatalanMiddleDot(text[i]) && text[i - 1] is 'l' or 'L' && i >= 2 && char.IsLetter(text[i - 2])) || (text[i] is (char)0x0140 or (char)0x013F && char.IsLetter(text[i - 1]))))
                    {
                        opportunities[i + 1] = LineBreakOpportunity.AutoHyphen;
                    }

                    continue;
                }

                if (text[i] is (char)0x2010 or '-')
                {
                    // (a soft hyphen before the hyphen does not make an opportunity before it: UAX #14 forbids the break before a hyphen)
                    var syllableBefore = char.IsLetter(text[i - 1]) || (text[i - 1] == SoftHyphen && i >= 2 && char.IsLetter(text[i - 2]));
                    var repeats = language == HyphenationLanguage.Pinyin || options.Hyphens == HyphensMode.Auto || text[i - 1] == SoftHyphen;
                    if (repeats && syllableBefore && char.IsLetter(text[i + 1]) && opportunities[i + 1] == LineBreakOpportunity.Allowed && opportunities[i] is LineBreakOpportunity.None or LineBreakOpportunity.Hyphen)
                    {
                        opportunities[i] = LineBreakOpportunity.Hyphen;
                        opportunities[i + 1] = LineBreakOpportunity.None;
                    }

                    continue;
                }

                if (language != HyphenationLanguage.Pinyin || !IsPinyinApostrophe(text[i]) || !char.IsLetter(text[i + 1]) || opportunities[i] is LineBreakOpportunity.Mandatory or LineBreakOpportunity.Allowed)
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

    /// <summary>Whether a character is the middle dot of a Catalan geminate l: U+00B7 MIDDLE DOT, or U+2027 HYPHENATION POINT that some texts use instead.</summary>
    private static bool IsCatalanMiddleDot(char c) => c is (char)0x00B7 or (char)0x2027;

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
            else if (primary.Equals("ca", StringComparison.OrdinalIgnoreCase))
            {
                result = HyphenationLanguage.Catalan;
            }
            else if (primary.Equals("de", StringComparison.OrdinalIgnoreCase) && subtags.Contains("1901", StringComparer.OrdinalIgnoreCase))
            {
                // The 1901 orthography of German (de-1901, de-CH-1901)
                result = HyphenationLanguage.German1901;
            }
            else if (RepeatedHyphenLanguages.Contains(primary, StringComparer.OrdinalIgnoreCase))
            {
                result = HyphenationLanguage.RepeatedHyphen;
            }
        }

        _lastHyphenationLanguageElement = element;
        _lastHyphenationLanguage = result;
        return result;
    }
}
