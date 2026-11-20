using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The language-specific hyphenation (https://drafts.csswg.org/css-text-4/#hyphens-property): "The UA must use the appropriate
// language-specific hyphenation character(s) and should apply any appropriate spelling changes just as for automatic hyphenation
// at the same point". The spelling changes of the WPT tests css/css-text/hyphens/i18n are applied at the soft hyphens and at the
// automatic hyphenation opportunities (no browser applies them): the Dutch diminutives of the words in -é (cafeetje is
// café-tje), the Dutch trema that separates two vowels, which is not needed at the start of the next line (zeeën is zee-en), the long double consonants of Hungarian (Összeg is Ösz-szeg), and the apostrophe that separates the syllables of
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
    /// line starts <see cref="RemovedAfter"/> characters after the break, with <see cref="InsertedAfter"/> before them (the
    /// letters of the spelling change that replace the removed characters, or <see langword="null"/>).
    /// </summary>
    private readonly record struct HyphenationBreak(int RemovedBefore, string Hyphen, int RemovedAfter, string? InsertedAfter = null);

    /// <summary>The digraphs of Hungarian whose long form doubles their first letter (e.g. ssz is the long sz), longest first.</summary>
    private static readonly string[] HungarianDigraphs = ["dzs", "cs", "dz", "gy", "ly", "ny", "sz", "ty", "zs"];

    /// <summary>The characters of the hyphenation opportunities of <see cref="AddLanguageHyphenationOpportunities"/>: the apostrophes and the hyphens.</summary>
    private static readonly System.Buffers.SearchValues<char> LanguageHyphenationCharacters = System.Buffers.SearchValues.Create("\u2019'\u2010-");

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

                // The trema that separates two vowels (zeeën, beïnvloeden) is not needed at the start of the next line (zee-en,
                // be-invloeden): the vowel loses it, precomposed or followed by U+0308 COMBINING DIAERESIS
                if (end > 0 && char.IsLetter(text[end - 1]) && position < text.Length)
                {
                    var vowel = GetDutchTremaVowel(text[position]);
                    if (vowel != 0 && position + 1 < text.Length && char.IsLetter(text[position + 1]))
                        return new HyphenationBreak(0, hyphen, 1, vowel.ToString());

                    if (text[position] is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U' && position + 2 < text.Length && text[position + 1] == CombiningDiaeresis && char.IsLetter(text[position + 2]))
                        return new HyphenationBreak(0, hyphen, 2, text[position].ToString());
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

    private const char CombiningDiaeresis = (char)0x0308;

    /// <summary>The vowel without its diaeresis of a precomposed vowel with a diaeresis of Dutch (ä, ë, ï, ö, ü), or 0.</summary>
    private static char GetDutchTremaVowel(char c) => c switch
    {
        (char)0x00E4 => 'a',
        (char)0x00EB => 'e',
        (char)0x00EF => 'i',
        (char)0x00F6 => 'o',
        (char)0x00FC => 'u',
        (char)0x00C4 => 'A',
        (char)0x00CB => 'E',
        (char)0x00CF => 'I',
        (char)0x00D6 => 'O',
        (char)0x00DC => 'U',
        _ => (char)0,
    };

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
        var removed = GetLineStartSpellingChange(start, out _, out _).RemovedAfter;
        return removed > 0 ? start with { Position = start.Position + removed } : start;
    }

    /// <summary>
    /// The spelling change at the start of the line that starts at <paramref name="start"/>, made by the hyphenation break that
    /// ends the previous line: the characters it removes (see <see cref="HyphenationBreak.RemovedAfter"/>) are in the text item
    /// <paramref name="item"/> (the word may continue in the next item: <c>&lt;span&gt;zee&amp;shy;ë&lt;/span&gt;n</c>), and
    /// <paramref name="style"/> is the style of the text before the break. A break without such a change has no removed characters.
    /// </summary>
    private HyphenationBreak GetLineStartSpellingChange(LinePosition start, out int item, out ComputedStyle? style)
    {
        item = -1;
        style = null;
        if (!_opportunitiesComputed || start.Position <= 0 || start.Position >= _length || _opportunities[start.Position] is not (LineBreakOpportunity.Hyphen or LineBreakOpportunity.AutoHyphen))
            return default;

        // (the style of the text before the break)
        var index = start.Item;
        while (index > 0 && (Items[index].Kind != InlineItemKind.Text || Items[index].Start >= start.Position))
        {
            index--;
        }

        if (Items[index].Kind != InlineItemKind.Text)
            return default;

        var hyphenBreak = GetHyphenationBreak(start.Position, Items[index].Style);
        if (hyphenBreak.RemovedAfter == 0)
            return default;

        // The removed characters must be in the text item that starts the line (the letters that replace them are shaped with its style)
        item = start.Item;
        while (item < Items.Length && Items[item].End <= start.Position)
        {
            item++;
        }

        var end = start.Position + hyphenBreak.RemovedAfter;
        if (item >= Items.Length || Items[item].Kind != InlineItemKind.Text || Items[item].Start > start.Position || end > Items[item].End)
        {
            item = -1;
            return default;
        }

        style = Items[index].Style;
        return hyphenBreak;
    }

    /// <summary>The width of the letters inserted at the start of the line that starts at <paramref name="start"/> by the spelling change of the hyphenation break that ends the previous line.</summary>
    private double MeasureLineStartSpellingChange(LinePosition start)
    {
        var hyphenBreak = GetLineStartSpellingChange(start, out var item, out _);
        return hyphenBreak.InsertedAfter is { } inserted ? TextShaper.MeasureWidth(inserted, Items[item].Style.Fonts, GetHyphenShapingOptions(Items[item].Style)) : 0;
    }

    /// <summary>
    /// Adds the letters inserted at the start of the line that starts at <paramref name="start"/> by the spelling change of the
    /// hyphenation break that ends the previous line (the vowel without its Dutch trema): a hyphen leaf in the inline box of the
    /// characters they replace, before the first text of the line, at the position of these characters. When these characters
    /// end their inline box, the leaf replaces the empty leaf of the box.
    /// </summary>
    private void AddLineStartSpellingChange(LinePosition start, bool isFirstLine)
    {
        var hyphenBreak = GetLineStartSpellingChange(start, out var item, out _);
        if (hyphenBreak.InsertedAfter is not { } inserted)
            return;

        var contentStart = start.Position + hyphenBreak.RemovedAfter;
        var container = Items[item].Container;
        for (var i = 0; i < _leaves.Count; i++)
        {
            var leaf = _leaves[i];
            var replacesEmptyBox = leaf.Kind == LineLeafKind.EmptyBox && ReferenceEquals(leaf.Box, container);
            if (!replacesEmptyBox)
            {
                if (leaf.Kind != LineLeafKind.Text)
                    continue;

                // (the first text of the line: the rest of the word, in the same item or after the end of the item)
                if (leaf.Item == item ? leaf.Start != contentStart : contentStart != Items[item].End)
                    return;
            }

            var style = GetLineStyle(Items[item], isFirstLine);
            var shaped = TextShaper.Shape(inserted, style.Fonts, GetHyphenShapingOptions(style) with { IsRightToLeft = (leaf.Level & 1) != 0 });
            var spellingChange = new LineLeaf { Kind = LineLeafKind.Hyphen, Level = leaf.Level, Item = item, Start = start.Position, End = start.Position, HangStart = start.Position, Parent = container, Shaped = shaped, GlyphCount = shaped.Glyphs.Length, Width = shaped.Width, SpellingChangeLength = hyphenBreak.RemovedAfter };
            if (replacesEmptyBox)
            {
                _leaves[i] = spellingChange;
            }
            else
            {
                _leaves.Insert(i, spellingChange);
            }

            return;
        }
    }

    /// <summary>
    /// Adds the hyphenation opportunities of the typographic conventions of the languages:
    /// <list type="bullet">
    /// <item>Pinyin: an apostrophe between two letters separates two syllables, a hyphenation opportunity of the automatic
    /// hyphenation (<c>hyphens: auto</c>), or of a soft hyphen before it (a quotation mark for the rules of UAX #14, which forbid
    /// the break before it).</item>
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
            if (language is not (HyphenationLanguage.Pinyin or HyphenationLanguage.RepeatedHyphen))
                continue;

            // (the character after the last one of the item may be in the next text item: a word may span inline boxes)
            for (var i = Math.Max(item.Start, 1); i < item.End && i + 1 < text.Length; i++)
            {
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

    /// <summary>
    /// Adds the automatic hyphenation opportunities of the geminate l of a Catalan word (l·l, or ŀl): the syllables are
    /// separated after the middle dot, which the hyphen replaces at the break (see <see cref="GetHyphenationBreak"/>). There is
    /// no Catalan dictionary. The limits are the ones of <see cref="HyphenationDictionary.FindOpportunities"/> (the minimum length
    /// of the word, and of the parts before and after the opportunity), and the word has no soft hyphen (see <see cref="HyphenateWord"/>).
    /// </summary>
    private static bool AddCatalanOpportunities(ReadOnlySpan<char> word, HyphenateLimitChars limits, Span<bool> points)
    {
        var prefix = limits.Before > 0 ? limits.Before : 2;
        var suffix = limits.After > 0 ? limits.After : prefix;
        if (word.Length < Math.Max(limits.Word > 0 ? limits.Word : 5, prefix + suffix))
            return false;

        var found = false;
        for (var i = Math.Max(prefix, 2); i <= word.Length - suffix; i++)
        {
            if (word[i] is not ('l' or 'L'))
                continue;

            if ((i >= 3 && IsCatalanMiddleDot(word[i - 1]) && word[i - 2] is 'l' or 'L' && char.IsLetter(word[i - 3]))
                || (word[i - 1] is (char)0x0140 or (char)0x013F && char.IsLetter(word[i - 2])))
            {
                points[i] = true;
                found = true;
            }
        }

        return found;
    }

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
