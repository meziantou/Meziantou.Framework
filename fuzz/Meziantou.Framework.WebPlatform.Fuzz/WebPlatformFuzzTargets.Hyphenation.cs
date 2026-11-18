using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the language-specific hyphenation (the spelling changes at a hyphenation break, the hyphen repeated at the
/// start of the next line): the input is decoded to a content language, a <c>hyphens</c> value, a width and a text made of the
/// characters that the rules of the languages look at. The boxes of the lines are finite, the layout is the same when it is
/// computed again, and the hyphenation does not change the rendered text (<c>innerText</c>), as it is "a rendering effect
/// only" (https://drafts.csswg.org/css-text-4/#hyphenation).
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private static readonly string[] HyphenationLanguages = ["en", "nl", "hu", "zh-Latn-pinyin", "ca", "de-1901", "de-CH-1901", "pl", "pt-BR", "es", "ug", "cr"];
    private static readonly string[] HyphenationModes = ["none", "manual", "auto"];

    /// <summary>
    /// The characters of the text: letters, spaces, the hyphens and the soft hyphen, the apostrophes of Pinyin, the middle dots
    /// and the l with middle dot of Catalan, an Arabic letter and a Canadian syllabic.
    /// </summary>
    private const string HyphenationAlphabet = "aelLcCkKtTjJsSzZbdginoru \u00E9\u00D6-\u2010\u00AD'\u2019\u00B7\u2027\u0140\u013F\u0628\u1472";

    /// <summary>
    /// The tokens of the target: the words of the spelling changes and the compound words, encoded as the input (the indexes of
    /// their characters in <see cref="HyphenationAlphabet"/>, each one byte in UTF-8).
    /// </summary>
    private static string[] HyphenationTokens => [.. new[] { "eetj", "\u00D6ssz", "l\u00B7l", "\u0140l", "ck", "a-a", "a\u00AD-a", "a\u2019a" }.Select(EncodeHyphenationText)];

    /// <summary>Decodes the input to a hyphenated text, and checks its layout.</summary>
    public static void Hyphenation(ReadOnlySpan<byte> data)
    {
        var position = 0;
        byte Next(ReadOnlySpan<byte> input) => position < input.Length ? input[position++] : (byte)0;

        var language = HyphenationLanguages[Next(data) % HyphenationLanguages.Length];
        var hyphens = HyphenationModes[Next(data) % HyphenationModes.Length];
        var width = (Next(data) % 200) + 1;
        var text = new StringBuilder();
        while (position < data.Length && text.Length < 200)
        {
            text.Append(HyphenationAlphabet[Next(data) % HyphenationAlphabet.Length]);
        }

        var markup = string.Create(CultureInfo.InvariantCulture, $"<div id=d lang={language} style='font: 10px sans-serif; width: {width}px; hyphens: {hyphens}'><span id=s>{text}</span></div><div id=n lang={language} style='font: 10px sans-serif; width: {width}px; hyphens: none'>{text}</div>");
        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => markup);
        context.EventLoop.RunUntilIdle();

        var span = document.GetElementById("s")!;
        var rects = span.GetClientRects();
        foreach (var rect in rects)
        {
            FuzzAssert.True(double.IsFinite(rect.X) && double.IsFinite(rect.Y) && double.IsFinite(rect.Width) && double.IsFinite(rect.Height), "the box of a line is finite");
            FuzzAssert.True(rect.Width >= 0 && rect.Height >= 0, "the size of the box of a line is not negative");
        }

        // The hyphenation is a rendering effect only: the rendered text is the one without hyphenation
        var hyphenated = (HtmlElement)document.GetElementById("d")!;
        var unhyphenated = (HtmlElement)document.GetElementById("n")!;
        FuzzAssert.Equal(unhyphenated.InnerText, hyphenated.InnerText, "the rendered text of the hyphenated text");

        // The layout does not depend on the previous layouts
        span.SetAttribute("data-relayout", "1");
        var again = span.GetClientRects();
        FuzzAssert.Equal(rects.Count, again.Count, "the number of boxes of the layout computed again");
        for (var i = 0; i < rects.Count; i++)
        {
            FuzzAssert.Equal(rects[i].X, again[i].X, "the position of a box of the layout computed again");
            FuzzAssert.Equal(rects[i].Width, again[i].Width, "the width of a box of the layout computed again");
        }
    }

    /// <summary>The seeds: the scenarios of the unit tests (the spelling changes and the repeated hyphens).</summary>
    private static IEnumerable<FuzzSeed> GetHyphenationSeeds()
    {
        (int Language, int Mode, int Width, string Text)[] scenarios =
        [
            (1, 1, 10, "cafee\u00ADtje"),
            (2, 1, 10, "\u00D6s\u00ADszeg"),
            (3, 1, 10, "t\u00E9\u00AD\u2019an"),
            (3, 1, 30, "ab-cd"),
            (4, 1, 10, "col\u00B7\u00ADlecci"),
            (4, 1, 10, "co\u0140\u00ADlecci"),
            (4, 2, 10, "col\u00B7lecci"),
            (5, 1, 10, "Zuc\u00ADker"),
            (6, 1, 10, "Zuc\u00ADker"),
            (7, 2, 30, "ab-cd"),
            (7, 1, 30, "ab\u00AD-cd"),
            (8, 2, 30, "ab-cd"),
            (9, 2, 30, "ab-cd"),
            (0, 2, 30, "ab-cd"),
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            var (language, mode, width, text) = scenarios[i];
            byte[] data = [(byte)language, (byte)mode, (byte)(width - 1), .. EncodeHyphenationText(text).Select(c => (byte)c)];
            yield return new FuzzSeed("hyphenation-" + i.ToString(CultureInfo.InvariantCulture), data);
        }
    }

    /// <summary>Encodes a text as the input of the target: the index of each character in <see cref="HyphenationAlphabet"/> (the first one for the characters that are not in it).</summary>
    private static string EncodeHyphenationText(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            result.Append((char)Math.Max(0, HyphenationAlphabet.IndexOf(c, StringComparison.Ordinal)));
        }

        return result.ToString();
    }
}
