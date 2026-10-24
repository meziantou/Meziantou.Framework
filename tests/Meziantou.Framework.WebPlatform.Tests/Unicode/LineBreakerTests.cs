using System.Text;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Unicode;

/// <summary>The CSS Text tailorings of the line breaking rules (https://drafts.csswg.org/css-text-4/#line-breaking).</summary>
public sealed class LineBreakerTests
{
    private const char SoftHyphen = (char)0x00AD;
    private const char NoBreakSpace = (char)0x00A0;
    private const char CombiningAcuteAccent = (char)0x0301;
    private const char ZeroWidthJoiner = (char)0x200D;

    private static string Text(params int[] codePoints)
    {
        var builder = new StringBuilder();
        foreach (var codePoint in codePoints)
        {
            builder.Append(char.ConvertFromUtf32(codePoint));
        }

        return builder.ToString();
    }

    // Formats the opportunities as the text with '|' at the soft wrap opportunities, '-' at the hyphenation opportunities,
    // '!' at the forced breaks, '~' at the emergency opportunities and '^' at the opportunities inside the phrases
    private static string Format(string text, LineBreakOptions options = default)
    {
        var opportunities = LineBreaker.GetOpportunities(text, options);
        var builder = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            builder.Append(opportunities[i] switch
            {
                LineBreakOpportunity.Allowed => "|",
                LineBreakOpportunity.Hyphen => "-",
                LineBreakOpportunity.Mandatory => "!",
                LineBreakOpportunity.Emergency => "~",
                LineBreakOpportunity.Phrase => "^",
                _ => "",
            });
            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    [Fact]
    public void Spaces()
    {
        Assert.Equal("Hello |world, |again", Format("Hello world, again"));
        Assert.Equal("a  |b", Format("a  b"));
        Assert.Equal("a\n!b", Format("a\nb"));
        Assert.Equal("a\r\n!b", Format("a\r\nb"));
        Assert.Equal("a" + NoBreakSpace + "b", Format("a" + NoBreakSpace + "b"));
        Assert.Equal("", Format(""));
    }

    // As in the browsers, a space is a soft wrap opportunity whatever follows it (UAX #14 forbids some of them, even after spaces)
    [Fact]
    public void Spaces_AreOpportunitiesWhateverFollowsThem()
    {
        Assert.Equal("a |.b |,c |)d |]e |!f |/g |( |" + (char)0x00AB + " |" + (char)0x2060 + "h", Format("a .b ,c )d ]e !f /g ( " + (char)0x00AB + " " + (char)0x2060 + "h"));
        Assert.Equal("a .b ,c )d ]e !|f /|g |( h", Format("a .b ,c )d ]e !f /g ( h", LineBreakOptions.UnicodeDefault));
    }

    [Fact]
    public void Punctuation()
    {
        Assert.Equal("(a) |[b] |c-|d |$12.50 |-1/12", Format("(a) [b] c-d $12.50 -1/12"));
    }

    // https://drafts.csswg.org/css-text-3/#line-break-details: the deviations from UAX #14 "for maximum interoperability with existing
    // implementations": no opportunity between "!" or "/" and a letter
    [Fact]
    public void Punctuation_DeviationsFromUnicode()
    {
        Assert.Equal("color: |red |!important", Format("color: red !important"));
        Assert.Equal("23/Jan/|2024", Format("23/Jan/2024"));
        Assert.Equal("!|important", Format("!important", LineBreakOptions.UnicodeDefault));
        Assert.Equal("23/|Jan", Format("23/Jan", LineBreakOptions.UnicodeDefault));

        // The letters of word-break: break-all are ideographic: the opportunities of UAX #14 after the punctuation remain
        Assert.Equal("!|a|b", Format("!ab", new LineBreakOptions { WordBreak = WordBreakMode.BreakAll }));
        Assert.Equal("2|3/|J|a|n", Format("23/Jan", new LineBreakOptions { WordBreak = WordBreakMode.BreakAll }));
    }

    [Fact]
    public void SoftHyphen_IsHyphenationOpportunity()
    {
        var text = "co" + SoftHyphen + "op";
        Assert.Equal("co" + SoftHyphen + "-op", Format(text));
        Assert.Equal("co" + SoftHyphen + "-op", Format(text, new LineBreakOptions { Hyphens = HyphensMode.Auto }));
        Assert.Equal(text, Format(text, new LineBreakOptions { Hyphens = HyphensMode.None }));
        Assert.Equal("co" + SoftHyphen + "-op", Format(text, new LineBreakOptions { WordBreak = WordBreakMode.AutoPhrase }));
        Assert.Equal("~c~o~" + SoftHyphen + "~o~p", "~" + Format(text, new LineBreakOptions { Hyphens = HyphensMode.None, OverflowWrap = OverflowWrapMode.Anywhere }));
    }

    [Fact]
    public void WordBreak_BreakAll()
    {
        var options = new LineBreakOptions { WordBreak = WordBreakMode.BreakAll };
        Assert.Equal("a|b|c |d|e|f", Format("abc def", options));
        Assert.Equal("1|2|3", Format("123", options));

        // Punctuation keeps its rules
        Assert.Equal("a|b, |c|d", Format("ab, cd", options));

        // A grapheme cluster is never broken
        Assert.Equal("e" + CombiningAcuteAccent + "|x", Format("e" + CombiningAcuteAccent + "x", options));
    }

    [Fact]
    public void WordBreak_KeepAll()
    {
        // With overflow-wrap: normal, "the restrictions introduced by word-break: keep-all may be relaxed to match word-break: normal
        // if there are no otherwise-acceptable break points in the line": the opportunities that keep-all removes are emergency ones
        var options = new LineBreakOptions { WordBreak = WordBreakMode.KeepAll };
        Assert.Equal("한|국|어 |텍|스|트", Format("한국어 텍스트"));
        Assert.Equal("한~국~어 |텍~스~트", Format("한국어 텍스트", options));
        Assert.Equal("日|本|語", Format("日本語"));
        Assert.Equal("日~本~語", Format("日本語", options));

        // The punctuation still allows breaks
        Assert.Equal("日~本。|語", Format("日本。語", options));

        // U+200B is never suppressed
        Assert.Equal("日~本" + (char)0x200B + "|語", Format("日本" + (char)0x200B + "語", options));
    }

    [Fact]
    public void WordBreak_SoutheastAsian()
    {
        // The words of Thai, Lao, Myanmar and Khmer are found with the word lists of ICU (lexical word breaking)
        Assert.Equal("ภาษา|ไทย|ภาษา|ไทย", Format("ภาษาไทยภาษาไทย"));
        Assert.Equal("กรุงเทพ|คือ|สวยงาม", Format("กรุงเทพคือสวยงาม"));
        Assert.Equal("ພາສາ|ລາວ|ພາສາ|ລາວ", Format("ພາສາລາວພາສາລາວ"));
        Assert.Equal("မြန်မာ|စာ|မြန်မာ|စာ", Format("မြန်မာစာမြန်မာစာ"));
        Assert.Equal("ភាសាខ្មែរ|ភាសាខ្មែរ", Format("ភាសាខ្មែរភាសាខ្មែរ"));

        // word-break: keep-all keeps the word boundaries; overflow-wrap adds emergency opportunities inside the words
        Assert.Equal("และ |และ|และ", Format("และ และและ", new LineBreakOptions { WordBreak = WordBreakMode.KeepAll }));
        Assert.Equal("~ภ~า~ษ~า|ไ~ท~ย", "~" + Format("ภาษาไทย", new LineBreakOptions { OverflowWrap = OverflowWrapMode.Anywhere }));

        // The other Southeast Asian scripts use the fallback breaking: a break between the typographic letter units, never inside
        // a grapheme cluster (U+1A60 TAI THAM SIGN SAKOT is a combining mark)
        Assert.Equal("ᨠ|ᨡ|ᨣ", Format("ᨠᨡᨣ"));

        // word-break: manual: no break (SA is treated as AL)
        Assert.Equal("ภาษาไทย", Format("ภาษาไทย", new LineBreakOptions { WordBreak = WordBreakMode.Manual }));
        Assert.Equal("ภาษาไทย", Format("ภาษาไทย", LineBreakOptions.UnicodeDefault));
    }

    [Fact]
    public void WordBreak_AutoPhrase()
    {
        // The phrases of Japanese are kept together: the other opportunities are only taken when the line would overflow
        var options = new LineBreakOptions { WordBreak = WordBreakMode.AutoPhrase, IsJapanese = true, IsChineseOrJapanese = true };
        Assert.Equal("東^京^へ|行^き^ま^し^ょ^う。", Format("東京へ行きましょう。", options));
        Assert.Equal("楽^し^い|ド^ラ^イ^ブ、|楽^し^い|ド^ラ^イ^ブ。", Format("楽しいドライブ、楽しいドライブ。", options));
        Assert.Equal("一^生^懸^命|働^き^ま^す。", Format("一生懸命働きます。", options));

        // U+200B and the spaces keep their opportunities; a letter before a phrase is part of it
        Assert.Equal("ド^ラ" + (char)0x200B + "|イ^ブ", Format("ドラ" + (char)0x200B + "イブ", options));
        Assert.Equal("ABC^東^京", Format("ABC東京", options));

        // Without a Japanese content language, auto-phrase is normal
        Assert.Equal("東|京|へ|行|き|ま|し|ょ|う。", Format("東京へ行きましょう。", new LineBreakOptions { WordBreak = WordBreakMode.AutoPhrase }));
    }

    [Fact]
    public void LineBreak_Anywhere()
    {
        var options = new LineBreakOptions { Strictness = LineBreakStrictness.Anywhere };
        Assert.Equal("a| |b|" + NoBreakSpace + "|c|,|d", Format("a b" + NoBreakSpace + "c,d", options));
        Assert.Equal("e" + CombiningAcuteAccent + "|x", Format("e" + CombiningAcuteAccent + "x", options));
        Assert.Equal("a\n!b", Format("a\nb", options));

        // An emoji ZWJ sequence is a single grapheme cluster
        var family = Text(0x1F468, 0x200D, 0x1F469, 0x200D, 0x1F467);
        Assert.Equal("a|" + family + "|b", Format("a" + family + "b", options));
    }

    [Fact]
    public void OverflowWrap_EmergencyOpportunities()
    {
        var options = new LineBreakOptions { OverflowWrap = OverflowWrapMode.Anywhere };
        Assert.Equal("a~b~c |d", Format("abc d", options));
        Assert.Equal("e" + CombiningAcuteAccent + "~x", Format("e" + CombiningAcuteAccent + "x", options));
        Assert.Equal("a~b~c |d", Format("abc d", options with { OverflowWrap = OverflowWrapMode.BreakWord }));
        Assert.Equal("abc |d", Format("abc d"));
    }

    [Fact]
    public void BreakSpaces()
    {
        var options = new LineBreakOptions { BreakSpaces = true };
        Assert.Equal("a |b", Format("a b", options));
        Assert.Equal("a | | |b", Format("a   b", options));
        Assert.Equal("a   |b", Format("a   b"));
    }

    [Fact]
    public void BreakSpaces_OtherSpaceSeparators()
    {
        // "after every preserved white space character and after every other space separator", but the no-break ones (GL)
        var options = new LineBreakOptions { BreakSpaces = true };
        Assert.Equal("a\U00003000|\U00003000|b", Format("a\U00003000\U00003000b", options));
        Assert.Equal("a\U00003000\U00003000|b", Format("a\U00003000\U00003000b"));
        Assert.Equal("a\U0000202Fb", Format("a\U0000202Fb", options));
    }

    [Fact]
    public void BreakSpaces_EmergencyOpportunityBeforeASpace()
    {
        // The spaces of break-spaces take up space: overflow-wrap may break before them
        var options = new LineBreakOptions { BreakSpaces = true, OverflowWrap = OverflowWrapMode.BreakWord };
        Assert.Equal("a~b~ |c", Format("ab c", options));
        Assert.Equal("a~b |c", Format("ab c", options with { BreakSpaces = false }));
    }

    [Fact]
    public void ControlCharacters()
    {
        // The control characters are ordinary characters (not combining marks); the ones of the classes BK and NL are forced breaks
        var options = new LineBreakOptions { OverflowWrap = OverflowWrapMode.Anywhere };
        Assert.Equal("a~\U00000001~b", Format("a\U00000001b", options));
        Assert.Equal("a\U0000000C!b", Format("a\U0000000Cb"));
        Assert.Equal("a\U00000085!b", Format("a\U00000085b"));
    }

    [Fact]
    public void ZeroWidthJoiner_PreventsBreaks()
    {
        Assert.Equal("a" + ZeroWidthJoiner + "b|日", Format("a" + ZeroWidthJoiner + "b日"));
    }

    [Fact]
    public void Runs_UseTheOptionsOfEachCharacter()
    {
        var text = "abcd";
        var opportunities = new LineBreakOpportunity[text.Length];
        new LineBreaker().FindOpportunities(text, [new LineBreakOptionsRun(0, default), new LineBreakOptionsRun(2, new LineBreakOptions { WordBreak = WordBreakMode.BreakAll })], opportunities);
        Assert.Equal([LineBreakOpportunity.None, LineBreakOpportunity.None, LineBreakOpportunity.Allowed, LineBreakOpportunity.Allowed], opportunities);
    }

    [Fact]
    public void Breaker_ReusesItsBuffers()
    {
        var breaker = new LineBreaker();
        var opportunities = new LineBreakOpportunity[20];
        breaker.FindOpportunities("aaaaaaaaaa bbbbbbbbb", default(LineBreakOptions), opportunities);
        Assert.Equal(LineBreakOpportunity.Allowed, opportunities[11]);
        breaker.FindOpportunities("a b", default(LineBreakOptions), opportunities);
        Assert.Equal([LineBreakOpportunity.None, LineBreakOpportunity.None, LineBreakOpportunity.Allowed], opportunities[..3]);
    }

    [Theory]
    [InlineData(0x65E5, 0x672C, true)] // Wide, Wide
    [InlineData(0xFF21, 0x65E5, true)] // Fullwidth, Wide
    [InlineData(0xFF71, 0x65E5, true)] // Halfwidth, Wide
    [InlineData('a', 0x65E5, false)]
    [InlineData(0x65E5, 'a', false)]
    [InlineData(0x2026, 0x65E5, false)] // Ambiguous
    [InlineData(0xD55C, 0xAD6D, false)] // Hangul syllables
    [InlineData(0x3131, 0x65E5, false)] // Hangul compatibility jamo (Wide)
    [InlineData('a', 0x200B, true)]
    [InlineData(0x200B, 'a', true)]
    [InlineData(0x306F, 0x20A9, false)] // Wide, U+20A9 WON SIGN (Halfwidth, Korean)
    [InlineData(0x65E5, 0x1F600, false)] // Wide, wide emoji
    public void SegmentBreakTransformation(int before, int after, bool removed)
    {
        Assert.Equal(removed, LineBreaker.IsSegmentBreakRemoved(before, after));
    }

    [Theory]
    [InlineData(0x3001, 'E', true)] // Wide punctuation (IDEOGRAPHIC COMMA), Latin
    [InlineData('e', 0x30FB, true)] // Latin, KATAKANA MIDDLE DOT
    [InlineData(0xFF64, 'I', true)] // HALFWIDTH IDEOGRAPHIC COMMA, Latin
    [InlineData(0x3002, '(', true)]
    [InlineData('x', 0xFF5E, true)] // FULLWIDTH TILDE (a symbol used as U+301C WAVE DASH)
    [InlineData(0x201C, 0x30A2, true)] // Ambiguous punctuation, Katakana
    [InlineData(0x30AF, 0x201D, true)]
    [InlineData(0x201C, 'a', false)]
    [InlineData(0x201C, 0xD55C, false)] // Ambiguous punctuation, Hangul
    [InlineData(0x306F, '$', false)] // Wide, Narrow symbol
    [InlineData(0x306F, 0x20AC, false)] // EURO SIGN (Ambiguous)
    [InlineData(0x306F, 'a', false)]
    public void SegmentBreakTransformation_ChineseJapaneseOrYi(int before, int after, bool removed)
    {
        Assert.Equal(removed, LineBreaker.IsSegmentBreakRemoved(before, after) || LineBreaker.IsSegmentBreakRemovedInChineseJapaneseOrYi(before, after));
    }

    // The tests of css/css-text/i18n (*/css-text-line-break-*): whether there is a soft wrap opportunity before the character in
    // "文文文文文文X字", for each strictness and content language. They follow https://drafts.csswg.org/css-text-4/#line-break-property:
    // - cj: breaks before the small kana are allowed with loose and normal, as in the browsers (the specification forbids them
    //   with normal since w3c/csswg-drafts#10363)
    // - pr: there is a break before the prefixes in every mode (rule LB31 of UAX #14; the specification only adds the breaks after
    //   them with loose), while the ja and zh WPT tests forbid them with normal and strict
    public static TheoryData<string, string, bool, bool, bool, bool> CjkStrictnessData()
    {
        var data = new TheoryData<string, string, bool, bool, bool, bool>();
        // The expected values are (loose, normal, strict), in Chinese or Japanese and in the other languages
        void Add(string category, int[] codePoints, (bool Loose, bool Normal, bool Strict) chineseOrJapanese, (bool Loose, bool Normal, bool Strict) other)
        {
            foreach (var codePoint in codePoints)
            {
                var hex = codePoint.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);
                data.Add(category, hex, true, chineseOrJapanese.Loose, chineseOrJapanese.Normal, chineseOrJapanese.Strict);
                data.Add(category, hex, false, other.Loose, other.Normal, other.Strict);
            }
        }

        Add("cj", [0x3041, 0x3043, 0x3045, 0x3047, 0x3049, 0x3063, 0x3083, 0x3085, 0x3087, 0x308E, 0x3095, 0x3096, 0x30A1, 0x30A3, 0x30A5, 0x30A7, 0x30A9, 0x30C3, 0x30E3, 0x30E5, 0x30E7, 0x30EE, 0x30F5, 0x30F6, 0x30FC, 0x31F0, 0x31F1, 0x31F2, 0x31F3, 0x31F4, 0x31F5, 0x31F6, 0x31F7, 0x31F8, 0x31F9, 0x31FA, 0x31FB, 0x31FC, 0x31FD, 0x31FE, 0x31FF, 0xFF67, 0xFF68, 0xFF69, 0xFF6A, 0xFF6B, 0xFF6C, 0xFF6D, 0xFF6E, 0xFF6F, 0xFF70], (true, true, false), (true, true, false));
        Add("cpm", [0x30FB, 0xFF1A, 0xFF1B, 0xFF65, 0x203C, 0x2047, 0x2048, 0x2049, 0xFF01, 0xFF1F], (true, false, false), (false, false, false));
        Add("in", [0x2024, 0x2025, 0x2026, 0x22EF, 0xFE19], (true, false, false), (true, false, false));
        Add("iteration", [0x3005, 0x303B, 0x309D, 0x309E, 0x30FD, 0x30FE], (true, false, false), (true, false, false));
        Add("po", [0x00B0, 0x2030, 0x2032, 0x2033, 0x2035, 0x2103, 0x2109, 0xFE6A, 0xFF05, 0xFFE0], (true, false, false), (false, false, false));
        Add("pr", [0x00B1, 0x20AC, 0x2116, 0xFE69, 0xFF04, 0xFFE1, 0xFFE5, 0xFFE6], (true, true, true), (true, true, true));
        return data;
    }

    [Theory]
    [MemberData(nameof(CjkStrictnessData))]
    public void LineBreak_Strictness(string category, string codePoint, bool chineseOrJapanese, bool loose, bool normal, bool strict)
    {
        _ = category;
        var text = "文文文文文文" + char.ConvertFromUtf32(int.Parse(codePoint, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture)) + "字";
        bool BreakBefore(LineBreakStrictness strictness)
            => LineBreaker.GetOpportunities(text, new LineBreakOptions { Strictness = strictness, IsChineseOrJapanese = chineseOrJapanese })[6] == LineBreakOpportunity.Allowed;

        Assert.Equal(loose, BreakBefore(LineBreakStrictness.Loose));
        Assert.Equal(normal, BreakBefore(LineBreakStrictness.Normal));
        Assert.Equal(strict, BreakBefore(LineBreakStrictness.Strict));
    }

    [Theory]
    [InlineData(0x301C)]
    [InlineData(0x30A0)]
    public void LineBreak_CjkHyphenLikeCharacters(int codePoint)
    {
        // Breaks before U+301C and U+30A0 are allowed with normal and loose in Chinese and Japanese
        var text = "文文" + (char)codePoint + "字";
        bool BreakBefore(LineBreakStrictness strictness, bool chineseOrJapanese)
            => LineBreaker.GetOpportunities(text, new LineBreakOptions { Strictness = strictness, IsChineseOrJapanese = chineseOrJapanese })[2] == LineBreakOpportunity.Allowed;

        Assert.True(BreakBefore(LineBreakStrictness.Loose, chineseOrJapanese: true));
        Assert.True(BreakBefore(LineBreakStrictness.Normal, chineseOrJapanese: true));
        Assert.False(BreakBefore(LineBreakStrictness.Strict, chineseOrJapanese: true));
        Assert.False(BreakBefore(LineBreakStrictness.Loose, chineseOrJapanese: false));
        Assert.False(BreakBefore(LineBreakStrictness.Normal, chineseOrJapanese: false));
    }

    [Theory]
    [InlineData(0x2010)]
    [InlineData(0x2013)]
    public void LineBreak_LooseHyphens(int codePoint)
    {
        // Breaks before U+2010 and U+2013 are allowed with loose after an ideographic character
        bool BreakBefore(string text, LineBreakStrictness strictness, WordBreakMode wordBreak = WordBreakMode.Normal)
            => LineBreaker.GetOpportunities(text, new LineBreakOptions { Strictness = strictness, WordBreak = wordBreak })[1] == LineBreakOpportunity.Allowed;

        Assert.True(BreakBefore("文" + (char)codePoint, LineBreakStrictness.Loose));
        Assert.False(BreakBefore("文" + (char)codePoint, LineBreakStrictness.Normal));
        Assert.False(BreakBefore("a" + (char)codePoint, LineBreakStrictness.Loose));
        Assert.True(BreakBefore("a" + (char)codePoint, LineBreakStrictness.Loose, WordBreakMode.BreakAll));
    }
}
