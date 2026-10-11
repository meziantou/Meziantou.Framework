namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <summary>
/// The synthetic fonts of the corpus (<c>fonts/synthetic/</c>) and of the tests. Their geometry is chosen so that the
/// expected values of a test can be computed by hand: 1024 units per em, so that a size of 16 pixels maps 64 units to
/// one pixel, and coordinates that are multiples of 64.
/// </summary>
internal static class FontSamples
{
    public const int NotdefGlyph = 0;
    public const int SpaceGlyph = 1;
    public const int BoxA = 2;
    public const int BoxB = 3;
    public const int BoxC = 4;
    public const int TriangleD = 5;
    public const int FrameE = 6;
    public const int OverlapF = 7;
    public const int Boxa = 8;
    public const int AcuteMark = 9;
    public const int CompositeAacute = 10;
    public const int ScaledH = 11;
    public const int HyphenGlyph = 12;

    /// <summary>
    /// A font made of straight-edged glyphs.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Metrics: ascender 832, descender -192, no line gap (a line is exactly one em); cap height 768, x-height 512; underline at -128, 64 thick; strikeout at 256, 64 thick.</description></item>
    /// <item><description>.notdef: a frame from (64, 0) to (448, 768), 64 thick; advance 512.</description></item>
    /// <item><description>Space: no outline; advance 512.</description></item>
    /// <item><description>A: the rectangle (128, 0)-(896, 768); advance 1024.</description></item>
    /// <item><description>B: the rectangle (64, 0)-(448, 512); advance 512.</description></item>
    /// <item><description>C: the rectangle (0, -192)-(768, 832), the full height of the line; advance 768.</description></item>
    /// <item><description>D: the triangle (0, 0), (256, 768), (512, 0); advance 512.</description></item>
    /// <item><description>E: a frame from (0, 0) to (1024, 768), 128 thick; advance 1024.</description></item>
    /// <item><description>F: two rectangles of the same direction that overlap, (0, 0)-(512, 512) and (256, 256)-(768, 768); advance 768.</description></item>
    /// <item><description>a: the rectangle (64, 0)-(448, 512); advance 512.</description></item>
    /// <item><description>U+0301: the rectangle (-640, 832)-(-384, 960), a mark that overhangs the previous glyph; advance 0.</description></item>
    /// <item><description>U+00C1: a composite of A, whose metrics it uses, and of the mark moved by (1152, 0).</description></item>
    /// <item><description>H: a composite of B scaled by 1.5 and moved by (64, 0), which is the rectangle (160, 0)-(736, 768); advance 1024.</description></item>
    /// <item><description>Hyphen-minus and U+2010: the rectangle (64, 192)-(448, 320); advance 512.</description></item>
    /// <item><description>Kerning (kern table): A then B is tightened by 128, B then A widened by 64.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder Boxes()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Boxes",
            PostScriptName = "MeziantouBoxes-Regular",
            UnitsPerEm = 1024,
            Ascender = 832,
            Descender = -192,
            LineGap = 0,
            TypoAscender = 832,
            TypoDescender = -192,
            TypoLineGap = 0,
            WinAscent = 832,
            WinDescent = 192,
            CapHeight = 768,
            XHeight = 512,
            UnderlinePosition = -128,
            UnderlineThickness = 64,
            StrikeoutPosition = 256,
            StrikeoutSize = 64,
            CmapFormats = FontCmapFormats.Format4 | FontCmapFormats.Format12,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(FontGlyph.Empty(512), ' ');
        font.AddGlyph(FontGlyph.Rectangle(1024, 128, 0, 896, 768), 'A');
        font.AddGlyph(FontGlyph.Rectangle(512, 64, 0, 448, 512), 'B');
        font.AddGlyph(FontGlyph.Rectangle(768, 0, -192, 768, 832), 'C');
        font.AddGlyph(FontGlyph.Polygons(512, [(0, 0), (256, 768), (512, 0)]), 'D');
        font.AddGlyph(FontGlyph.Frame(1024, 0, 0, 1024, 768, 128), 'E');
        font.AddGlyph(FontGlyph.Polygons(768, [(0, 0), (0, 512), (512, 512), (512, 0)], [(256, 256), (256, 768), (768, 768), (768, 256)]), 'F');
        font.AddGlyph(FontGlyph.Rectangle(512, 64, 0, 448, 512), 'a');
        font.AddGlyph(FontGlyph.Rectangle(0, -640, 832, -384, 960), 0x0301);
        font.AddGlyph(
            FontGlyph.Composite(
                1024,
                new FontComponent { GlyphId = BoxA, UseMyMetrics = true },
                new FontComponent { GlyphId = AcuteMark, Argument1 = 1152, Argument2 = 0 }),
            0x00C1);
        font.AddGlyph(FontGlyph.Composite(1024, new FontComponent { GlyphId = BoxB, Argument1 = 64, XScale = 1.5, YScale = 1.5 }), 'H');
        font.AddGlyph(FontGlyph.Rectangle(512, 64, 192, 448, 320), '-');
        font.CharacterMap[0x2010] = HyphenGlyph;
        font.CharacterMap[0x00A0] = SpaceGlyph;

        font.KerningPairs[(BoxA, BoxB)] = -128;
        font.KerningPairs[(BoxB, BoxA)] = 64;
        return font;
    }

    /// <summary>
    /// A font whose glyphs have quadratic curves, in the forms a TrueType outline takes.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>O: four off-curve points and no on-curve point, (512, 0), (1024, 512), (512, 1024), (0, 512): the on-curve points are the midpoints of the sides.</description></item>
    /// <item><description>P: a contour that starts with an off-curve point: off (0, 512), on (256, 768), on (768, 768), off (1024, 512), on (768, 0), on (256, 0).</description></item>
    /// <item><description>Q: two consecutive off-curve points between on-curve points: on (0, 0), off (0, 512), off (512, 1024), on (1024, 1024), on (1024, 0).</description></item>
    /// <item><description>R: an outer square and a round hole made of four off-curve points, wound the other way.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder Curves()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Curves",
            PostScriptName = "MeziantouCurves-Regular",
            UnitsPerEm = 1024,
            Ascender = 1024,
            Descender = 0,
            TypoAscender = 1024,
            TypoDescender = 0,
            WinAscent = 1024,
            WinDescent = 0,
            CapHeight = 1024,
            XHeight = 512,
            GlyfEncoding = FontGlyfEncoding.Long,
            UseLongLoca = true,
            CmapFormats = FontCmapFormats.Format12,
        };

        font.AddGlyph(FontGlyph.Frame(512, 64, 0, 448, 768, 64));
        font.AddGlyph(Contours(1024, [[new(512, 0, false), new(0, 512, false), new(512, 1024, false), new(1024, 512, false)]]), 'O');
        font.AddGlyph(Contours(1024, [[new(0, 512, false), new(256, 768), new(768, 768), new(1024, 512, false), new(768, 0), new(256, 0)]]), 'P');
        font.AddGlyph(Contours(1024, [[new(0, 0), new(0, 512, false), new(512, 1024, false), new(1024, 1024), new(1024, 0)]]), 'Q');
        font.AddGlyph(
            Contours(
                1024,
                [
                    [new(0, 0), new(0, 1024), new(1024, 1024), new(1024, 0)],
                    [new(512, 256, false), new(768, 512, false), new(512, 768, false), new(256, 512, false)],
                ]),
            'R');
        return font;
    }

    private static FontGlyph Contours(int advanceWidth, FontPoint[][] contours)
    {
        var glyph = new FontGlyph { AdvanceWidth = advanceWidth };
        foreach (var contour in contours)
        {
            glyph.Contours.Add([.. contour]);
        }

        return glyph;
    }
}
