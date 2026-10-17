using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.UnicodeFuzz;

public static partial class UnicodeFuzzTargets
{
    private static readonly UnicodeLineBreakStrictness[] Strictnesses = [UnicodeLineBreakStrictness.Strict, UnicodeLineBreakStrictness.Normal, UnicodeLineBreakStrictness.Anywhere];
    private static readonly UnicodeWordBreakBehavior[] WordBreaks = [UnicodeWordBreakBehavior.Normal, UnicodeWordBreakBehavior.BreakAll, UnicodeWordBreakBehavior.KeepAll];

    /// <summary>
    /// Grapheme clusters, word segments and script runs are contiguous, non-empty and cover the text; a script run never
    /// ends inside a grapheme cluster. The line breaks are increasing, the last one is the end of the text and is
    /// mandatory, the mandatory breaks do not depend on the tailoring, and <see cref="UnicodeLineBreakStrictness.Anywhere"/>
    /// breaks at every grapheme cluster boundary. Nothing splits a surrogate pair.
    /// </summary>
    private static void Segmentation(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var graphemeBoundaries = GetGraphemeBoundaries(text);

        var expectedStart = 0;
        foreach (var word in Unicode.EnumerateWordBoundaries(text))
        {
            (_, expectedStart) = CheckSegment(text, word, expectedStart, "word segment");
        }

        FuzzAssert.Equal(text.Length, expectedStart, "The word segments do not cover the text");

        expectedStart = 0;
        foreach (var run in Unicode.EnumerateScriptRuns(text))
        {
            var (_, end) = CheckSegment(text, new Range(run.Index, run.Index + run.Length), expectedStart, $"script run ({run.Script})");
            FuzzAssert.True(graphemeBoundaries[end], $"The script run {run.Index}..{end} ({run.Script}) ends inside a grapheme cluster");
            expectedStart = end;
        }

        FuzzAssert.Equal(text.Length, expectedStart, "The script runs do not cover the text");

        string? expectedMandatory = null;
        foreach (var strictness in Strictnesses)
        {
            foreach (var wordBreak in WordBreaks)
            {
                var breaks = GetLineBreaks(text, dictionary: null, strictness, wordBreak);
                var mandatory = FormatPositions(breaks.Where(item => item.IsMandatory).Select(item => item.Index));
                expectedMandatory ??= mandatory;
                FuzzAssert.Equal(expectedMandatory, mandatory, $"The mandatory breaks with {strictness} and {wordBreak} differ from those with the default rules");

                if (strictness is UnicodeLineBreakStrictness.Anywhere)
                {
                    var expected = FormatPositions(Enumerable.Range(1, text.Length).Where(index => graphemeBoundaries[index]));
                    FuzzAssert.Equal(expected, FormatPositions(breaks.Select(item => item.Index)), $"The breaks with Anywhere and {wordBreak} are not the grapheme cluster boundaries");
                }
            }
        }
    }

    /// <summary>Enumerates the line breaks and checks that they are increasing, after the start, never inside a surrogate pair, and that the last one is the end of the text and is mandatory.</summary>
    private static List<UnicodeLineBreak> GetLineBreaks(string text, LineBreakDictionary? dictionary, UnicodeLineBreakStrictness strictness, UnicodeWordBreakBehavior wordBreak)
    {
        var result = new List<UnicodeLineBreak>();
        var previous = 0;
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(text, dictionary, strictness, wordBreak))
        {
            FuzzAssert.True(lineBreak.Index > previous && lineBreak.Index <= text.Length, $"The line break at {lineBreak.Index} is not after the previous one ({previous}) or is after the end of the text ({text.Length}), with {strictness} and {wordBreak}");
            FuzzAssert.False(SplitsSurrogatePair(text, lineBreak.Index), $"The line break at {lineBreak.Index} is inside a surrogate pair, with {strictness} and {wordBreak}");
            previous = lineBreak.Index;
            result.Add(lineBreak);
            FuzzAssert.True(result.Count <= text.Length, "There are more line breaks than characters");
        }

        if (text.Length == 0)
        {
            FuzzAssert.True(result.Count == 0, "An empty text has a line break");
        }
        else
        {
            FuzzAssert.True(result.Count > 0 && result[^1].Index == text.Length && result[^1].IsMandatory, $"The last line break is not a mandatory break at the end of the text, with {strictness} and {wordBreak}");
        }

        return result;
    }

    private static IEnumerable<FuzzSeed> GetSegmentationSeeds()
    {
        foreach (var file in (string[])["GraphemeBreakTest.txt.gz", "WordBreakTest.txt.gz", "LineBreakTest.txt.gz"])
        {
            foreach (var (index, line) in Sample(ReadConformanceLines(file), 120))
            {
                yield return FuzzSeed.FromText($"{file.Split('.')[0]}-{index}", ParseCodePoints(line));
            }
        }

        foreach (var (name, text) in HandWrittenTexts)
        {
            yield return FuzzSeed.FromText(name, text);
        }
    }

    private static readonly (string Name, string Text)[] HandWrittenTexts =
    [
        ("empty", ""),
        ("ascii", "Hello, world! It's 3.14 o'clock (really) - isn't it?\r\nSecond line\n"),
        ("emoji-zwj", "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 \U0001F44D\U0001F3FB \U0001F1EB\U0001F1F7\U0001F1E9\U0001F1EA 1\uFE0F\u20E3"),
        ("combining", "e\u0301\u0301\u0323 a\u0308 \u1E0A\u0323 A\u030A \u212B"),
        ("hangul", "\u1100\u1161\u11A8 \uAC00\uAC01 \u1100\u1100\u1161"),
        ("indic", "\u0915\u094D\u0937 \u0915\u093F \u0928\u092E\u0938\u094D\u0924\u0947"),
        ("thai", "\u0E20\u0E32\u0E29\u0E32\u0E44\u0E17\u0E22 \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35\u0E04\u0E23\u0E31\u0E1A"),
        ("cjk", "\u65E5\u672C\u8A9E\u3002\u3041\u30FC\u300C\u5F15\u7528\u300D\u3001\u30AB\u30BF\u30AB\u30CA"),
        ("mixed-scripts", "Latin \u0395\u03BB\u03BB\u03B7\u03BD\u03B9\u03BA\u03AC \u0420\u0443\u0441\u0441\u043A\u0438\u0439 \u05E2\u05D1\u05E8\u05D9\u05EA (\u0627\u0644\u0639\u0631\u0628\u064A\u0629) [x]"),
        ("lone-surrogates", "a\uD800b\uDC00c\uDBFF\U000103FF"),
        ("controls", "\u0000\u0001\u000B\u000C\u0085\u2028\u2029\r\r\n\n\u00AD\u200B\u2060\uFEFF\uFFFC"),
        ("numbers", "$1,234.56 -7% 1/2 \u0661\u0662\u0663 \u06F1.\u06F2 12:30"),
        ("tags", "\U0001F3F4\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F"),
    ];
}
