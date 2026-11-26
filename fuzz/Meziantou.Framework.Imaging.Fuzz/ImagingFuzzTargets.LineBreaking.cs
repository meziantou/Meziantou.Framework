using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The longest text of <see cref="LineBreakingTarget"/>: each input lays it out twice and reads the caret of each of its positions.</summary>
    private const int MaxLineBreakingText = 512;

    /// <summary>
    /// The font of <see cref="LineBreakingTarget"/>: the PostScript Type 1 font of the font tests (A is 600 units wide, O
    /// and H 700, x 500, E 600, the space 250), so that the text goes through the Type 1 reader too.
    /// </summary>
    private static readonly Lazy<Font> LineBreakingFont = new(() => Font.Load(FontType1Writer.Sample().WritePfb()));

    /// <summary>The characters that break lines or change how they break, and the letters of the font.</summary>
    /// <remarks>A property: <see cref="All"/>, in another part of the class, may be initialized first.</remarks>
    private static string[] LineBreakingTokens =>
    [
        " ", "  ", "\u00A0", "\u00AD", "\u200B", "\u2060", "\t", "\n", "\r\n", "\u2028", "\u2029", "-", "\u2014", "/",
        "A", "O", "H", "x", "E", "AO", "AAA AA AA AAAAA", "\u00C1", "\u05D0\u05D1", "\u0627\u0644", "\u4E00\u4E8C",
        "\u3001", "\u0301", "\uFB01", "\u200D", "\uD83D\uDE00",
    ];

    /// <summary>
    /// <see cref="TextLineBreakStrategy.Optimal"/> line breaking of a text read from the input, compared with greedy
    /// breaking: the lines cover the text in order, the same layout is found twice and keeping the glyphs of the parts of
    /// the text cut into lines gives the layout shaping them again gives, every caret position is finite, the
    /// lines fit the width whenever greedy breaking makes them fit, and, for text aligned at the start without
    /// hyphenation, the sum of the squares of the room left at the end of the lines but the last one is never more than
    /// with greedy breaking. The input is a byte of width (8 to 263 pixels, 16 pixels per em), a byte of options (bit 0:
    /// justified, bit 1: soft hyphens break, bit 2: words are cut anywhere, bit 3: right to left, bit 4: a first line
    /// indent, bit 5: the words alternate between two sizes, bit 6: the first word is a ruby base), then the text.
    /// </summary>
    private static void LineBreakingTarget(ReadOnlySpan<byte> data)
    {
        var width = 8 + FuzzInput.ReadByte(ref data);
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        if (text.Length > MaxLineBreakingText)
        {
            text = text[..MaxLineBreakingText];
            if (char.IsHighSurrogate(text[^1]))
            {
                text = text[..^1];
            }
        }

        if (text.Length == 0)
            return;

        var isJustified = (flags & 1) != 0;
        var hyphenation = (flags & 2) != 0 ? TextHyphenation.Manual : TextHyphenation.None;
        var cutsWords = (flags & 4) != 0;
        var rich = CreateLineBreakingText(text, (flags & 32) != 0, (flags & 64) != 0);
        if (rich is null)
            return;

        TextLayoutOptions CreateOptions(TextLineBreakStrategy strategy) => new()
        {
            MaxWidth = width,
            LineBreakStrategy = strategy,
            Alignment = isJustified ? TextAlignment.Justify : TextAlignment.Start,
            Hyphenation = hyphenation,
            OverflowWrap = cutsWords ? TextOverflowWrap.Anywhere : TextOverflowWrap.Normal,
            Direction = (flags & 8) != 0 ? TextDirection.RightToLeft : TextDirection.Auto,
            FirstLineIndent = (flags & 16) != 0 ? 12 : 0,
        };

        var greedy = TextLayout.Create(rich, CreateOptions(TextLineBreakStrategy.Greedy));
        var optimal = TextLayout.Create(rich, CreateOptions(TextLineBreakStrategy.Optimal));
        var again = TextLayout.Create(rich, CreateOptions(TextLineBreakStrategy.Optimal));
        FuzzAssert.True(optimal.Lines.SequenceEqual(again.Lines), "Optimal line breaking is not deterministic");

        var spans = rich.SpanList.ToArray();
        var optimalOptions = CreateOptions(TextLineBreakStrategy.Optimal);
        var kept = TextLayoutEngine.Create(rich.Text, spans, optimalOptions, 1, optimalOptions.MaxWidth, optimalOptions.MaxHeight);
        var shaped = TextLayoutEngine.Create(rich.Text, spans, optimalOptions, 1, optimalOptions.MaxWidth, optimalOptions.MaxHeight, reusesShapedGlyphs: false);
        FuzzAssert.True(
            kept.Lines.SequenceEqual(shaped.Lines) && kept.Glyphs.SequenceEqual(shaped.Glyphs) && kept.Positions.SequenceEqual(shaped.Positions) && kept.HitBoxes.SequenceEqual(shaped.HitBoxes),
            "Keeping the glyphs of the parts of the text cut into lines gives another layout than shaping them again");

        var position = 0;
        foreach (var line in optimal.Lines)
        {
            FuzzAssert.Equal(position, line.Start, "A line does not start where the previous one ends");
            position += line.Length;
        }

        FuzzAssert.Equal(text.Length, position, "The lines do not cover the text");
        for (var index = 0; index <= text.Length; index++)
        {
            var caret = optimal.GetCaretRectangle(index);
            FuzzAssert.True(double.IsFinite(caret.X) && double.IsFinite(caret.Y), "A caret position is not finite");
        }

        // The indent of the first line of a paragraph is outside its width. Without cutting words, the breaks of greedy
        // breaking are break opportunities: when its lines fit, lines that fit exist, and optimal breaking chooses them.
        var indent = (flags & 16) != 0 ? 12 : 0;
        if (cutsWords || !greedy.Lines.All(Fits))
            return;

        FuzzAssert.True(optimal.Lines.All(Fits), "Optimal line breaking makes a line wider than its width where greedy breaking does not");
        if (isJustified || hyphenation != TextHyphenation.None || indent != 0 || text.Contains('\t', StringComparison.Ordinal))
            return;

        // A paragraph costs the sum of the squares of the room at the end of its lines but the last one
        FuzzAssert.True(GetRoom(optimal) <= GetRoom(greedy) * (1 + 1e-9) + 1e-6, "Optimal line breaking leaves more room at the end of the lines than greedy breaking");

        double GetRoom(TextLayout layout)
        {
            var room = 0.0;
            for (var i = 0; i < layout.LineCount; i++)
            {
                var line = layout.Lines[i];
                var end = line.Start + line.Length;
                if (end < text.Length && !EndsParagraph(text, end))
                {
                    room += (width - line.Width) * (width - line.Width);
                }
            }

            return room;
        }

        // The white space that ends a wrapped line hangs, but not the one that ends a paragraph: it is in the width of
        // the line, and no strategy makes it fit
        bool Fits(TextLine line)
        {
            var end = line.Start + line.Length;
            var endsWithSpace = line.Length > 0 && (end == text.Length || EndsParagraph(text, end)) && char.IsWhiteSpace(text[end - 1]);
            return endsWithSpace || line.Width <= width - (line.Start == 0 || EndsParagraph(text, line.Start) ? indent : 0) + 1e-6;
        }
    }

    private static bool IsLineBreak(char character) => character is '\n' or '\r' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029';

    /// <summary>Gets whether a line that ends at a position ends its paragraph: the last line of a paragraph costs nothing.</summary>
    private static bool EndsParagraph(string text, int end) => end > 0 && IsLineBreak(text[end - 1]);

    /// <summary>
    /// Creates the text of <see cref="LineBreakingTarget"/>: in one style, or with the words in two sizes, and with its
    /// first word as a ruby base.
    /// </summary>
    /// <returns>The text, or <see langword="null"/> when its first word cannot be a ruby base.</returns>
    private static RichText? CreateLineBreakingText(string text, bool mixedSizes, bool ruby)
    {
        var font = LineBreakingFont.Value;
        var normal = new TextStyle(font, 16);
        var largeStyle = new TextStyle(font, 24);
        var builder = new RichTextBuilder();
        var start = 0;
        if (ruby)
        {
            var end = text.IndexOf(' ', StringComparison.Ordinal);
            end = end < 0 ? text.Length : end;
            if (end == 0)
                return null;

            try
            {
                builder.AppendRuby(text[..end], normal, "AA", new TextStyle(font, 8));
            }
            catch (ArgumentException)
            {
                // A base that cannot be one, such as one that ends a paragraph
                return null;
            }

            start = end;
        }

        var large = false;
        while (start < text.Length)
        {
            var end = text.IndexOf(' ', start + 1, StringComparison.Ordinal);
            end = !mixedSizes || end < 0 ? text.Length : end;
            builder.Append(text[start..end], large ? largeStyle : normal);
            large = mixedSizes && !large;
            start = end;
        }

        return builder.Build();
    }

    private static IEnumerable<FuzzSeed> GetLineBreakingSeeds()
    {
        // The cases of the unit tests: a paragraph whose lines optimal breaking fills more evenly than greedy breaking
        // (A is 9.6 pixels at 16 pixels per em and the space 4: greedy breaking fills the first line, "AAA AA", 48
        // pixels, and leaves 28.8 pixels at the end of the second one), justified, in two paragraphs, indented, right to
        // left; a word wider than the line, cut; a soft hyphen; mixed sizes, bidi text and ruby; a long paragraph
        yield return Seed("even", 48, 0, "AAA AA AA AAAAA");
        yield return Seed("justified", 48, 1, "AAA AA AA AAAAA");
        yield return Seed("paragraphs", 48, 0, "AAA AA AA AAAAA\nAAA AA AA AAAAA");
        yield return Seed("indented", 60, 16, "AAA AA AA AAAAA");
        yield return Seed("right-to-left", 48, 8, "AAA AA AA AAAAA");
        yield return Seed("overflow", 40, 0, "AAAAAA H AAAAAA");
        yield return Seed("cut-anywhere", 40, 4, "AAAAAA H AAAAAA");
        yield return Seed("soft-hyphen", 50, 2, "AA AA\u00ADAA AA");
        yield return Seed("mixed-sizes", 90, 32, "AO HxE AAA O EE xx AO H");
        yield return Seed("bidi", 90, 0, "AO \u05D0\u05D1\u05D2 HxE \u05D0 AA \u0627\u0644\u0639 O");
        yield return Seed("ruby", 80, 64, "\u4E00\u4E8C AO HH \u4E00 AOA");
        yield return Seed("all-options", 120, 0x7F, "\u4E00\u4E8C AO\u00AD HH \u05D0\u05D1 AOA\tx E\u2028AA");
        yield return Seed("long", 200, 0, string.Concat(Enumerable.Repeat("AAA AA AA AAAAA ", 30)));

        static FuzzSeed Seed(string name, int width, byte flags, string text) => new(name, (byte[])[(byte)(width - 8), flags, .. FuzzText.Encode(text)]);
    }
}
