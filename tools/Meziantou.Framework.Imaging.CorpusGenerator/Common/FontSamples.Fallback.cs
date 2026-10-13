namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <content>The synthetic fonts made as fonts were before OpenType Layout: without substitution and positioning tables.</content>
internal static partial class FontSamples
{
    /// <summary>
    /// A font of Arabic made as before OpenType Layout: it has no substitution table, and has the letters of Arabic and
    /// their presentation forms as characters.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>The space, then a glyph for each character of U+0621 to U+0652, U+0670 to U+06D3, U+FB50 to U+FBFF, U+FC00 to U+FC03, U+FD3C, U+FD3D, U+FDF2 and U+FE70 to U+FEFC, in this order: the glyph of U+0621 is the glyph 2.</description></item>
    /// <item><description>Each glyph is the rectangle (64, 0)-(448, 512), with an advance of 512; a mark (U+064B to U+0652, U+0670) is the rectangle (-384, 640)-(-128, 768) and has no advance.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder ArabicForms()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Arabic Forms",
            PostScriptName = "MeziantouArabicForms-Regular",
            UnitsPerEm = 1024,
            Ascender = 832,
            Descender = -192,
            TypoAscender = 832,
            TypoDescender = -192,
            WinAscent = 832,
            WinDescent = 192,
            CmapFormats = FontCmapFormats.Format4,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(FontGlyph.Empty(512), ' ');
        foreach (var (first, last) in ((int, int)[])[(0x0621, 0x0652), (0x0670, 0x06D3), (0xFB50, 0xFBFF), (0xFC00, 0xFC03), (0xFD3C, 0xFD3D), (0xFDF2, 0xFDF2), (0xFE70, 0xFEFC)])
        {
            for (var character = first; character <= last; character++)
            {
                var isMark = character is (>= 0x064B and <= 0x0652) or 0x0670;
                font.AddGlyph(isMark ? FontGlyph.Rectangle(0, -384, 640, -128, 768) : FontGlyph.Rectangle(512, 64, 0, 448, 512), character);
            }
        }

        return font;
    }

    /// <summary>The marks of <see cref="Marks"/> that are drawn above the baseline, at one place: one or two of each kind of combining class.</summary>
    public static ReadOnlySpan<int> MarksAbove =>
    [
        0x0300, 0x0301, 0x0315, 0x031B, 0x0321, 0x0316, 0x0334, 0x0345, 0x035C, 0x0361, 0x302A, 0x3099, 0x093C, 0x0C55,
        0x059A, 0x05AE, 0x05B0, 0x05B9, 0x05BC, 0x05BF, 0x05C1, 0x05C2, 0x064B, 0x064D, 0x0651, 0x0670, 0x0711,
        0x0E38, 0x0E3A, 0x0E48, 0x0EB8, 0x0EC8, 0x0F71, 0x0F72, 0x0F74,
    ];

    /// <summary>
    /// A font made as before OpenType Layout, without substitution and positioning tables, whose marks are all drawn at
    /// one place whatever they are: 1000 units per em.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>A: the rectangle (100, 0)-(700, 700), advance 1000: its ink is not in the middle of its advance. p: (50, -200)-(550, 500), advance 600. i: (100, 0)-(200, 700), advance 300. Space: advance 250.</description></item>
    /// <item><description>The Hebrew alef (U+05D0): (50, 0)-(350, 400), advance 600. The Arabic beh (U+0628) and the Thai ko kai (U+0E01): (50, 0)-(550, 400), advance 600.</description></item>
    /// <item><description>The marks of <see cref="MarksAbove"/>: the rectangle (-250, 800)-(-150, 900), without advance. They are 62 units higher than a mark that stands on A at a sixteenth of the em.</description></item>
    /// <item><description>U+0302, a mark above that is drawn low: (-250, 720)-(-150, 780). U+0317, a mark below that is drawn low: (-250, -300)-(-150, -200). U+0323, a mark below that is drawn high: (-250, -40)-(-150, 0). U+0308, a mark above that has an advance of 200: (-250, 800)-(-150, 900).</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder Marks()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Marks",
            PostScriptName = "MeziantouMarks-Regular",
            UnitsPerEm = 1000,
            Ascender = 800,
            Descender = -200,
            TypoAscender = 800,
            TypoDescender = -200,
            WinAscent = 800,
            WinDescent = 200,
            CmapFormats = FontCmapFormats.Format4,
        };

        font.AddGlyph(FontGlyph.Rectangle(500, 50, 0, 450, 700));
        font.AddGlyph(FontGlyph.Empty(250), ' ');
        font.AddGlyph(FontGlyph.Rectangle(1000, 100, 0, 700, 700), 'A');
        font.AddGlyph(FontGlyph.Rectangle(600, 50, -200, 550, 500), 'p');
        font.AddGlyph(FontGlyph.Rectangle(300, 100, 0, 200, 700), 'i');
        font.AddGlyph(FontGlyph.Rectangle(600, 50, 0, 350, 400), 0x05D0);
        font.AddGlyph(FontGlyph.Rectangle(600, 50, 0, 550, 400), 0x0628);
        font.AddGlyph(FontGlyph.Rectangle(600, 50, 0, 550, 400), 0x0E01);
        foreach (var mark in MarksAbove)
        {
            font.AddGlyph(FontGlyph.Rectangle(0, -250, 800, -150, 900), mark);
        }

        font.AddGlyph(FontGlyph.Rectangle(0, -250, 720, -150, 780), 0x0302);
        font.AddGlyph(FontGlyph.Rectangle(0, -250, -300, -150, -200), 0x0317);
        font.AddGlyph(FontGlyph.Rectangle(0, -250, -40, -150, 0), 0x0323);
        font.AddGlyph(FontGlyph.Rectangle(200, -250, 800, -150, 900), 0x0308);
        return font;
    }

    /// <summary>
    /// A font of Thai made as before OpenType Layout: it has no substitution table, and has the forms of its marks and
    /// of two consonants as characters of the private use area.
    /// </summary>
    /// <param name="useMacCodePoints">Whether the forms are at the code points of the fonts of Mac OS (U+F884 to U+F89E) instead of those of the fonts of Windows (U+F700 to U+F71A).</param>
    /// <remarks>
    /// The space, then a glyph for each character of U+0E01 to U+0E5B, then one for each character of the private use
    /// range, in this order: the glyph of U+0E01 is the glyph 2. Each glyph is the rectangle (64, 0)-(448, 512), with an
    /// advance of 512; the marks have no advance.
    /// </remarks>
    public static FontBuilder ThaiForms(bool useMacCodePoints = false)
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Thai Forms",
            PostScriptName = "MeziantouThaiForms-Regular",
            UnitsPerEm = 1024,
            Ascender = 832,
            Descender = -192,
            TypoAscender = 832,
            TypoDescender = -192,
            WinAscent = 832,
            WinDescent = 192,
            CmapFormats = FontCmapFormats.Format4,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(FontGlyph.Empty(512), ' ');
        foreach (var (first, last) in ((int, int)[])[(0x0E01, 0x0E5B), useMacCodePoints ? (0xF884, 0xF89E) : (0xF700, 0xF71A)])
        {
            for (var character = first; character <= last; character++)
            {
                var isMark = character is 0x0E31 or (>= 0x0E34 and <= 0x0E3A) or (>= 0x0E47 and <= 0x0E4E) or > 0xF000 and not (0xF700 or 0xF70F or 0xF89A or 0xF89E);
                font.AddGlyph(isMark ? FontGlyph.Rectangle(0, -384, 640, -128, 768) : FontGlyph.Rectangle(512, 64, 0, 448, 512), character);
            }
        }

        return font;
    }
}
