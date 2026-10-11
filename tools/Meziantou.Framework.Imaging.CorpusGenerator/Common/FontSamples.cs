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

    /// <summary>
    /// A font whose points are at every kind of distance from the previous one, for the formats that encode a point by
    /// the size of that distance (the flags of the glyph table, the coordinate encodings of WOFF2).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Glyph 1 (A): one contour whose steps are listed in the method, straight and curved.</description></item>
    /// <item><description>Glyph 2 (B): a square with instructions, flagged as overlapping.</description></item>
    /// <item><description>Glyph 3 (C): the two previous glyphs as components, the second one far away, with instructions.</description></item>
    /// <item><description>Glyph 4 (space): no outline.</description></item>
    /// <item><description>Glyph 5 (D): two contours of one and two points.</description></item>
    /// </list>
    /// The instructions push a value and pop it: they are valid and change nothing.
    /// </remarks>
    public static FontBuilder Deltas()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Deltas",
            PostScriptName = "MeziantouDeltas-Regular",
            UnitsPerEm = 2048,
            CmapFormats = FontCmapFormats.Format4,
        };

        // Each step is the distance from the previous point; the first one is the first point.
        (int X, int Y, bool OnCurve)[] steps =
        [
            // One coordinate only, 8 bits and a base of 0, 256, ... 1024.
            (0, 1, true), (0, -1, true), (0, 255, true), (0, 256, false), (0, -511, true), (0, 1279, true), (0, -1279, false),
            (1, 0, true), (-1, 0, true), (255, 0, false), (256, 0, true), (-511, 0, true), (1279, 0, true), (-1279, 0, true),

            // Both coordinates, from 1 to 64.
            (1, 1, true), (-1, -1, false), (16, -17, true), (-17, 16, true), (64, 64, true), (-64, -64, true), (33, -48, false), (-48, 33, true),

            // Both coordinates, from 1 to 768.
            (65, 1, true), (-1, -65, true), (257, -300, false), (-300, 257, true), (768, 768, true), (-768, -768, true), (513, -256, true), (-256, 513, false),

            // Both coordinates under 4096, then any.
            (769, 5, true), (-5, -769, true), (0, 1280, true), (-1280, 0, false), (4095, -4095, true), (-4095, 4095, true),
            (4096, 1, true), (-1, -4096, false), (0, 4096, true), (-4096, 0, true), (5000, -6000, true), (-5000, 6000, true), (20000, 10000, false), (-20000, -10000, true),
        ];

        var contour = new List<FontPoint>();
        var x = 0;
        var y = 0;
        foreach (var step in steps)
        {
            x += step.X;
            y += step.Y;
            contour.Add(new FontPoint(x, y, step.OnCurve));
        }

        font.AddGlyph(FontGlyph.Rectangle(1024, 128, 0, 896, 1536));
        var path = new FontGlyph { AdvanceWidth = 2048 };
        path.Contours.Add(contour);
        var pathGlyph = font.AddGlyph(path, 'A');

        var square = FontGlyph.Rectangle(1024, 100, 0, 900, 800);
        square.Instructions = [0xB0, 0x00, 0x21];
        square.OverlapSimple = true;
        var squareGlyph = font.AddGlyph(square, 'B');

        var composite = FontGlyph.Composite(
            2048,
            new FontComponent { GlyphId = pathGlyph },
            new FontComponent { GlyphId = squareGlyph, Argument1 = 3000, Argument2 = -2000 });
        composite.Instructions = [0xB0, 0x01, 0x21];
        font.AddGlyph(composite, 'C');
        font.AddGlyph(FontGlyph.Empty(512), ' ');
        font.AddGlyph(Contours(1024, [[new(10, 10)], [new(300, 20), new(500, 700, false)]]), 'D');
        return font;
    }

    /// <summary>
    /// An OpenType font with CFF outlines: one glyph per group of operators of the Type 2 charstring format. The
    /// outline each glyph must give is in <c>FontTests</c>, computed by hand from the charstrings below.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>Glyph 0 (.notdef): a width, then rmoveto and rlineto.</description></item>
    /// <item><description>Glyph 1 (A): hmoveto, vmoveto, hlineto and vlineto, whose lines alternate.</description></item>
    /// <item><description>Glyph 2 (B): rrcurveto, rcurveline and rlinecurve.</description></item>
    /// <item><description>Glyph 3 (C): vvcurveto and hhcurveto, with and without their first argument.</description></item>
    /// <item><description>Glyph 4 (D): hvcurveto and vhcurveto, with one and two curves, with and without the last argument.</description></item>
    /// <item><description>Glyph 5 (E): hflex, flex, hflex1 and the two cases of flex1.</description></item>
    /// <item><description>Glyph 6 (F): a width, stem hints, a hint mask with implicit vertical stems, a counter mask.</description></item>
    /// <item><description>Glyph 7 (G): local and global subroutines, a subroutine that calls another one, a subroutine that ends the glyph.</description></item>
    /// <item><description>Glyph 8 (H): the encodings of the numbers. The fixed-point ones are whole: FreeType rounds the other ones.</description></item>
    /// <item><description>Glyph 9 (I): a contour that draws nothing, a contour that returns to its start, a contour of one line.</description></item>
    /// <item><description>Glyph 10 (space): no outline.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder Charstrings()
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Charstrings",
            PostScriptName = "MeziantouCharstrings-Regular",
            UnitsPerEm = 1000,
            CmapFormats = FontCmapFormats.Format4,
        };

        var cff = new FontCffWriter();
        cff.LocalSubroutines[0].Add(new FontCharstring().Op(FontCharstring.RLineTo, 100, 0).Op(FontCharstring.Return).ToArray());
        cff.LocalSubroutines[0].Add(new FontCharstring().Op(FontCharstring.RLineTo, 0, 100).Call(FontCharstring.CallGSubr, 0, subroutineCount: 2).Op(FontCharstring.Return).ToArray());
        cff.GlobalSubroutines.Add(new FontCharstring().Op(FontCharstring.RLineTo, -100, 0).Op(FontCharstring.Return).ToArray());
        cff.GlobalSubroutines.Add(new FontCharstring().Op(FontCharstring.EndChar).ToArray());

        void Add(int advance, int codePoint, FontCharstring charstring)
        {
            font.AddGlyph(FontGlyph.Empty(advance), codePoint);
            cff.CharStrings.Add(charstring.ToArray());
        }

        Add(500, -1, new FontCharstring()
            .Op(FontCharstring.RMoveTo, 500, 100, 0)
            .Op(FontCharstring.RLineTo, 0, 700, 300, 0, 0, -700)
            .Op(FontCharstring.EndChar));
        Add(600, 'A', new FontCharstring()
            .Op(FontCharstring.HMoveTo, 50)
            .Op(FontCharstring.HLineTo, 100, 200, -100)
            .Op(FontCharstring.VMoveTo, 100)
            .Op(FontCharstring.VLineTo, 100, 100, -100)
            .Op(FontCharstring.EndChar));
        Add(600, 'B', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 0, 0)
            .Op(FontCharstring.RRCurveTo, 0, 100, 100, 100, 100, 0, 100, 0, 100, -100, 0, -100)
            .Op(FontCharstring.RCurveLine, 0, -50, -50, -50, -50, 0, -100, 0)
            .Op(FontCharstring.RLineCurve, -100, 0, -50, 0, -50, 50, 0, 50)
            .Op(FontCharstring.EndChar));
        Add(600, 'C', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 100, 100)
            .Op(FontCharstring.VVCurveTo, 10, 50, 20, 30, 40)
            .Op(FontCharstring.VVCurveTo, 10, 20, 30, 40, -10, -20, -30, -40)
            .Op(FontCharstring.HHCurveTo, 5, 10, 20, 30, 40)
            .Op(FontCharstring.HHCurveTo, -10, -20, -30, -40)
            .Op(FontCharstring.EndChar));
        Add(600, 'D', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 0, 0)
            .Op(FontCharstring.HVCurveTo, 10, 20, 30, 40)
            .Op(FontCharstring.HVCurveTo, 10, 20, 30, 40, 50, 60, 70, 80)
            .Op(FontCharstring.HVCurveTo, 10, 20, 30, 40, 5)
            .Op(FontCharstring.VHCurveTo, 10, 20, 30, 40)
            .Op(FontCharstring.VHCurveTo, 10, 20, 30, 40, 50, 60, 70, 80, 7)
            .Op(FontCharstring.EndChar));
        Add(600, 'E', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 0, 0)
            .Escape(FontCharstring.HFlex, 10, 20, 30, 40, 50, 60, 70)
            .Escape(FontCharstring.Flex, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 50)
            .Escape(FontCharstring.HFlex1, 1, 2, 3, 4, 5, 6, 7, 8, 9)
            .Escape(FontCharstring.Flex1, 10, 1, 10, 1, 10, 1, 10, 1, 10, 1, 20)
            .Escape(FontCharstring.Flex1, 1, 10, 1, 10, 1, 10, 1, 10, 1, 10, 20)
            .Op(FontCharstring.EndChar));
        Add(400, 'F', new FontCharstring()
            .Op(FontCharstring.HStemHM, 400, 10, 20, 30, 40)
            .Op(FontCharstring.VStemHM, 10, 20)
            .Op(FontCharstring.HintMask, 50, 60).Raw(0xE0)
            .Op(FontCharstring.RMoveTo, 10, 10)
            .Op(FontCharstring.RLineTo, 100, 0, 0, 100)
            .Op(FontCharstring.CntrMask).Raw(0x80)
            .Op(FontCharstring.RLineTo, -100, 0)
            .Op(FontCharstring.EndChar));
        Add(600, 'G', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 20, 20)
            .Call(FontCharstring.CallSubr, 0, subroutineCount: 2)
            .Call(FontCharstring.CallSubr, 1, subroutineCount: 2)
            .Call(FontCharstring.CallGSubr, 1, subroutineCount: 2)
            .Op(FontCharstring.RLineTo, 500, 500));
        Add(600, 'H', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 108, -108)
            .Op(FontCharstring.RLineTo, 1131, -1131, 1132, -1132)
            .Fixed(2).Fixed(-100).Op(FontCharstring.RLineTo)
            .Op(FontCharstring.EndChar));
        Add(600, 'I', new FontCharstring()
            .Op(FontCharstring.RMoveTo, 10, 10)
            .Op(FontCharstring.RMoveTo, 5, 5)
            .Op(FontCharstring.RLineTo, 10, 0, 0, 10, -10, -10)
            .Op(FontCharstring.HMoveTo, 100)
            .Op(FontCharstring.RLineTo, 10, 10)
            .Op(FontCharstring.EndChar));
        Add(250, ' ', new FontCharstring().Op(FontCharstring.EndChar));
        cff.Apply(font);
        return font;
    }

    /// <summary>
    /// An OpenType font with CFF outlines whose glyphs are identified by CID: two font DICTs, each with its own local
    /// subroutines. The subroutine 0 draws a horizontal line in the first one and a vertical line in the second one,
    /// so the outline of a glyph shows which font DICT it was decoded with.
    /// </summary>
    public static FontBuilder CidKeyed(int fontDictSelectFormat = 3)
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou CID",
            PostScriptName = "MeziantouCID-Regular",
            UnitsPerEm = 1000,
            CmapFormats = FontCmapFormats.Format4,
        };

        var cff = new FontCffWriter { FontDictOfGlyphs = [0, 1, 1, 0], FontDictSelectFormat = fontDictSelectFormat };
        cff.LocalSubroutines[0].Add(new FontCharstring().Op(FontCharstring.RLineTo, 100, 0).Op(FontCharstring.Return).ToArray());
        cff.LocalSubroutines.Add([new FontCharstring().Op(FontCharstring.RLineTo, 0, 100).Op(FontCharstring.Return).ToArray()]);

        void Add(int codePoint, int x, int y, int dx, int dy)
        {
            font.AddGlyph(FontGlyph.Empty(500), codePoint);
            cff.CharStrings.Add(new FontCharstring()
                .Op(FontCharstring.RMoveTo, x, y)
                .Call(FontCharstring.CallSubr, 0, subroutineCount: 1)
                .Op(FontCharstring.RLineTo, dx, dy)
                .Op(FontCharstring.EndChar)
                .ToArray());
        }

        Add(-1, 0, 0, 0, 50);
        Add('A', 0, 0, 50, 0);
        Add('B', 10, 10, 50, 0);
        Add('C', 10, 10, 0, 50);
        cff.Apply(font);
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
