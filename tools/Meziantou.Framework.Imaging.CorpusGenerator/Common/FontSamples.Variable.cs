namespace Meziantou.Framework.Imaging.CorpusGenerator.Common;

/// <content>The synthetic variable fonts.</content>
internal static partial class FontSamples
{
    public const int VariableSpace = 1;
    public const int VariableA = 2;
    public const int VariableB = 3;
    public const int VariableC = 4;
    public const int VariableD = 5;
    public const int VariableE = 6;
    public const int VariableF = 7;
    public const int VariableG = 8;
    public const int VariableH = 9;

    /// <summary>
    /// A variable font with TrueType outlines: 1000 units per em and two axes, the weight (100 to 900, 400 by default)
    /// and the width (50 to 200, 100 by default).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>avar</c> table bends the weight: the normalized value 0.5 (a weight of 650) becomes 0.25, and the values
    /// between are interpolated, so 0.75 (775) becomes 0.625. The width is not mapped.
    /// </para>
    /// <para>Below, "w+" is the region from the default to the heaviest weight, "w-" to the lightest, "d+" to the widest width and "d-" to the narrowest.</para>
    /// <list type="bullet">
    /// <item><description>Space: no outline; advance 300, 300 more at d+ (its only points are the phantom points).</description></item>
    /// <item><description>A: the rectangle (100, 0)-(500, 700), advance 600; a delta for every point in five regions. w+: left -20, right +60, advance +40. w-: left +30, right -30, advance -40. d+: right +400, advance +400. d-: right -200, advance -200. w+ and d+ together: right +50, advance +50.</description></item>
    /// <item><description>B: the rectangle (50, 0)-(450, 500), advance 500; in w+, a delta for two points only, (50, 500) by (0, +100) and (450, 0) by (+80, 0), and for the advance (+80). The two other corners are inferred: (50, 0) stays, (450, 500) moves by (+80, +100).</description></item>
    /// <item><description>C: the hexagon (0, 0), (0, 600), (300, 600), (600, 600), (600, 0), (300, 0), advance 700. d+ and d- give a delta to (0, 0), which stays, to (600, 600), by (+200, +100) and (-100, 0), and to the advance, by +200 and -100, with point numbers they share; the points between are interpolated. A region of the weight that starts at 0.25, peaks at 0.5 and ends at 1 raises every point by 40.</description></item>
    /// <item><description>D: two contours, the first one with a curve; in w+, a delta for the control point of the curve only, (0, +100): the whole first contour follows, the second one stays.</description></item>
    /// <item><description>E: a composite of A and of B moved by (600, 0), advance 1100. The offset of B and the advance vary: w+ (+40, +10) and +120, w- -40, d+ +400, d- -200, w+ and d+ together +50.</description></item>
    /// <item><description>F: a composite of A, whose metrics it uses, and of B scaled by 0.5 and moved by (700, 0); in d+, the offset of B moves by +400.</description></item>
    /// <item><description>G: the rectangle (100, 0)-(500, 700) with a left side bearing of 60, advance 600: it is drawn from 60 to 460. In the font without <c>HVAR</c> table, its origin and its advance point move by +20 in w+: it is drawn 20 units further left, and its advance does not change.</description></item>
    /// <item><description>H: a polygon of ten points, advance 800; in d+, deltas of one byte, of two bytes and runs of zeros, and +300 for the advance.</description></item>
    /// <item><description>Advances (<c>HVAR</c>): the same deltas as the phantom points, through a delta set index map and two subtables.</description></item>
    /// <item><description>Metrics (<c>MVAR</c>), w+ and w-: ascender +50 and -20, descender -30 and +10, line gap +12 and 0, x-height +20 and 0, cap height +10 and 0, underline offset -20 and 0, underline size +30 and -10, strikeout offset +15 and 0, strikeout size +25 and -5.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="withAdvanceVariations">Whether the font has an <c>HVAR</c> table; without it, the advances vary with the phantom points of the glyphs.</param>
    public static FontBuilder Variable(bool withAdvanceVariations = true)
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Variable",
            PostScriptName = "MeziantouVariable-Regular",
            UnitsPerEm = 1000,
            Ascender = 800,
            Descender = -200,
            LineGap = 0,
            TypoAscender = 800,
            TypoDescender = -200,
            TypoLineGap = 0,
            WinAscent = 800,
            WinDescent = 200,
            CapHeight = 700,
            XHeight = 500,
            UnderlinePosition = -100,
            UnderlineThickness = 50,
            StrikeoutPosition = 250,
            StrikeoutSize = 50,
            ShareVariationTuples = true,
        };

        font.Axes.Add(new FontAxisDefinition("wght", 100, 400, 900, "Weight") { Map = [(-1, -1), (0, 0), (0.5, 0.25), (1, 1)] });
        font.Axes.Add(new FontAxisDefinition("wdth", 50, 100, 200, "Width"));
        font.NamedInstances.Add(new FontNamedInstanceDefinition("Light", [100, 100]));
        font.NamedInstances.Add(new FontNamedInstanceDefinition("Bold", [900, 100], "MeziantouVariable-Bold"));
        font.NamedInstances.Add(new FontNamedInstanceDefinition("Wide", [400, 200]));

        double[] heavier = [1, 0];
        double[] lighter = [-1, 0];
        double[] wider = [0, 1];
        double[] narrower = [0, -1];
        double[] heavierAndWider = [1, 1];

        font.AddGlyph(FontGlyph.Rectangle(500, 50, 0, 450, 700));

        var space = FontGlyph.Empty(300);
        space.Variations.Add(new FontTupleVariation { Peak = wider, Deltas = [(0, 0), (300, 0), (0, 0), (0, 0)] });
        font.AddGlyph(space, ' ');

        var a = FontGlyph.Rectangle(600, 100, 0, 500, 700);
        a.Variations.Add(new FontTupleVariation { Peak = heavier, Deltas = [(-20, 0), (-20, 0), (60, 0), (60, 0), (0, 0), (40, 0), (0, 0), (0, 0)] });
        a.Variations.Add(new FontTupleVariation { Peak = lighter, Deltas = [(30, 0), (30, 0), (-30, 0), (-30, 0), (0, 0), (-40, 0), (0, 0), (0, 0)] });
        a.Variations.Add(new FontTupleVariation { Peak = wider, Deltas = [(0, 0), (0, 0), (400, 0), (400, 0), (0, 0), (400, 0), (0, 0), (0, 0)] });
        a.Variations.Add(new FontTupleVariation { Peak = narrower, Deltas = [(0, 0), (0, 0), (-200, 0), (-200, 0), (0, 0), (-200, 0), (0, 0), (0, 0)] });
        a.Variations.Add(new FontTupleVariation { Peak = heavierAndWider, Deltas = [(0, 0), (0, 0), (50, 0), (50, 0), (0, 0), (50, 0), (0, 0), (0, 0)] });
        font.AddGlyph(a, 'A');

        var b = FontGlyph.Rectangle(500, 50, 0, 450, 500);
        b.Variations.Add(new FontTupleVariation { Peak = heavier, Points = [1, 3, 5], Deltas = [(0, 100), (80, 0), (80, 0)] });
        font.AddGlyph(b, 'B');

        var c = FontGlyph.Polygons(700, [(0, 0), (0, 600), (300, 600), (600, 600), (600, 0), (300, 0)]);
        c.Variations.Add(new FontTupleVariation { Peak = wider, Points = [0, 3, 7], UsesSharedPoints = true, Deltas = [(0, 0), (200, 100), (200, 0)] });
        c.Variations.Add(new FontTupleVariation { Peak = [0.5, 0], Start = [0.25, 0], End = [1, 0], Deltas = [(0, 40), (0, 40), (0, 40), (0, 40), (0, 40), (0, 40), (0, 0), (0, 0), (0, 0), (0, 0)] });
        c.Variations.Add(new FontTupleVariation { Peak = narrower, Points = [0, 3, 7], UsesSharedPoints = true, Deltas = [(0, 0), (-100, 0), (-100, 0)] });
        font.AddGlyph(c, 'C');

        var d = Contours(600, [
            [new(100, 0), new(100, 400), new(300, 600, false), new(500, 400), new(500, 0)],
            [new(200, 100), new(400, 100), new(400, 300), new(200, 300)],
        ]);
        d.Variations.Add(new FontTupleVariation { Peak = heavier, Points = [2], WritePointsAsWords = true, Deltas = [(0, 100)] });
        font.AddGlyph(d, 'D');

        var e = FontGlyph.Composite(1100, new FontComponent { GlyphId = VariableA }, new FontComponent { GlyphId = VariableB, Argument1 = 600, Argument2 = 0 });
        e.Variations.Add(new FontTupleVariation { Peak = heavier, Deltas = [(0, 0), (40, 10), (0, 0), (120, 0), (0, 0), (0, 0)] });
        e.Variations.Add(new FontTupleVariation { Peak = lighter, Deltas = [(0, 0), (-40, 0), (0, 0), (-40, 0), (0, 0), (0, 0)] });
        e.Variations.Add(new FontTupleVariation { Peak = wider, Deltas = [(0, 0), (400, 0), (0, 0), (400, 0), (0, 0), (0, 0)] });
        e.Variations.Add(new FontTupleVariation { Peak = narrower, Deltas = [(0, 0), (-200, 0), (0, 0), (-200, 0), (0, 0), (0, 0)] });
        e.Variations.Add(new FontTupleVariation { Peak = heavierAndWider, Deltas = [(0, 0), (50, 0), (0, 0), (50, 0), (0, 0), (0, 0)] });
        font.AddGlyph(e, 'E');

        var f = FontGlyph.Composite(
            600,
            new FontComponent { GlyphId = VariableA, UseMyMetrics = true },
            new FontComponent { GlyphId = VariableB, Argument1 = 700, Argument2 = 0, XScale = 0.5, YScale = 0.5 });
        f.Variations.Add(new FontTupleVariation { Peak = wider, Points = [1], Deltas = [(400, 0)] });
        font.AddGlyph(f, 'F');

        var g = FontGlyph.Rectangle(600, 100, 0, 500, 700);
        g.LeftSideBearing = 60;
        if (!withAdvanceVariations)
        {
            // With an HVAR table, FreeType takes the side bearings from it and HarfBuzz from the phantom points: the
            // glyph only varies its origin in the font where the two agree.
            g.Variations.Add(new FontTupleVariation { Peak = heavier, Points = [4, 5], Deltas = [(20, 0), (20, 0)] });
        }
        font.AddGlyph(g, 'G');

        var h = FontGlyph.Polygons(800, [(0, 0), (0, 700), (100, 700), (100, 400), (300, 400), (300, 700), (400, 700), (400, 0), (300, 0), (100, 0)]);
        h.Variations.Add(new FontTupleVariation
        {
            Peak = wider,
            Deltas = [(0, 0), (0, 0), (0, 0), (0, 0), (100, 0), (100, 0), (300, 5), (300, 0), (200, -5), (0, 0), (0, 0), (300, 0), (0, 0), (0, 0)],
        });
        font.AddGlyph(h, 'H');

        if (withAdvanceVariations)
        {
            // The regions w+, w-, d+, d- and the corner of w+ and d+; the second subtable only has d+.
            var store = new FontVariationStore();
            store.Regions.Add(new FontVariationRegion((0, 1, 1), (0, 0, 0)));
            store.Regions.Add(new FontVariationRegion((-1, -1, 0), (0, 0, 0)));
            store.Regions.Add(new FontVariationRegion((0, 0, 0), (0, 1, 1)));
            store.Regions.Add(new FontVariationRegion((0, 0, 0), (-1, -1, 0)));
            store.Regions.Add(new FontVariationRegion((0, 1, 1), (0, 1, 1)));

            // The widest deltas come first: the columns of d+ and d- need 16 bits.
            var first = new FontVariationSubtable { Regions = [2, 3, 0, 1, 4] };
            first.Items.Add([0, 0, 0, 0, 0]);
            first.Items.Add([400, -200, 40, -40, 50]);
            first.Items.Add([0, 0, 80, 0, 0]);
            first.Items.Add([200, -100, 0, 0, 0]);
            first.Items.Add([400, -200, 120, -40, 50]);
            store.Subtables.Add(first);
            var second = new FontVariationSubtable { Regions = [2] };
            second.Items.Add([300]);
            store.Subtables.Add(second);
            font.AdvanceVariations = store;
            font.AdvanceVariationMap = FontVariationWriter.WriteIndexMap([(0, 0), (1, 0), (0, 1), (0, 2), (0, 3), (0, 0), (0, 4), (0, 1), (0, 0), (1, 0)]);
        }

        var metrics = new FontVariationStore();
        metrics.Regions.Add(new FontVariationRegion((0, 1, 1), (0, 0, 0)));
        metrics.Regions.Add(new FontVariationRegion((-1, -1, 0), (0, 0, 0)));
        var metricItems = new FontVariationSubtable { Regions = [0, 1] };
        void AddMetric(string tag, int heavierDelta, int lighterDelta)
        {
            font.MetricVariationItems[tag] = (0, metricItems.Items.Count);
            metricItems.Items.Add([heavierDelta, lighterDelta]);
        }

        AddMetric("hasc", 50, -20);
        AddMetric("hdsc", -30, 10);
        AddMetric("hlgp", 12, 0);
        AddMetric("xhgt", 20, 0);
        AddMetric("cpht", 10, 0);
        AddMetric("undo", -20, 0);
        AddMetric("unds", 30, -10);
        AddMetric("stro", 15, 0);
        AddMetric("strs", 25, -5);
        metrics.Subtables.Add(metricItems);
        font.MetricVariations = metrics;
        return font;
    }

    /// <summary>
    /// A variable font with CFF2 outlines: 1000 units per em and one axis, the weight (100 to 900, 400 by default).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The variation store has the regions w+ (from the default to the heaviest weight) and w- (to the lightest). Its
    /// first subtable has both, in this order; its second one has w+ only. The first font DICT blends with the first
    /// subtable, the second font DICT with the second one.
    /// </para>
    /// <list type="bullet">
    /// <item><description>.notdef: the rectangle (50, 0)-(450, 700), without blend.</description></item>
    /// <item><description>A: the rectangle (100, 0)-(500, 700); its left side moves by -20 at w+ and +30 at w-, its width by +60 and -60.</description></item>
    /// <item><description>B, second font DICT: the rectangle (50, 0)-(450, 500), drawn by a local subroutine that blends; its height moves by +100 at w+ and its width by +80.</description></item>
    /// <item><description>C: selects the second subtable itself, then draws the triangle (0, 0), (300, 600), (600, 0), whose top moves by (0, +100) at w+.</description></item>
    /// <item><description>D: a closed curve from (100, 0); each of its six arguments has a delta at w+ and at w-, blended in one operator.</description></item>
    /// <item><description>Space: no outline.</description></item>
    /// <item><description>Advances (<c>HVAR</c>): A +40 and -40, B +80 at w+.</description></item>
    /// </list>
    /// </remarks>
    public static FontBuilder VariableCharstrings(int fontDictSelectFormat = 3)
    {
        var font = new FontBuilder
        {
            FamilyName = "Meziantou Variable Charstrings",
            PostScriptName = "MeziantouVariableCharstrings-Regular",
            UnitsPerEm = 1000,
        };
        font.Axes.Add(new FontAxisDefinition("wght", 100, 400, 900, "Weight"));

        var store = new FontVariationStore();
        store.Regions.Add(new FontVariationRegion((0, 1, 1)));
        store.Regions.Add(new FontVariationRegion((-1, -1, 0)));
        store.Subtables.Add(new FontVariationSubtable { Regions = [0, 1] });
        store.Subtables.Add(new FontVariationSubtable { Regions = [0] });

        var cff = new FontCff2Writer { VariationStore = store, FontDictOfGlyphs = [0, 0, 1, 0, 0, 0], FontDictSelectFormat = fontDictSelectFormat };
        cff.VariationStoreIndexes[1] = 1;
        cff.LocalSubroutines.Add([
            new FontCharstring()
                .Numbers(500, 100, 1).Op(FontCharstring.Blend)
                .Op(FontCharstring.VLineTo)
                .Numbers(400, 80, 1).Op(FontCharstring.Blend)
                .Op(FontCharstring.HLineTo)
                .Numbers(-500, -100, 1).Op(FontCharstring.Blend)
                .Op(FontCharstring.VLineTo)
                .ToArray(),
        ]);

        void Add(int advance, int codePoint, FontCharstring charstring)
        {
            font.AddGlyph(FontGlyph.Empty(advance), codePoint);
            cff.CharStrings.Add(charstring.ToArray());
        }

        Add(500, -1, new FontCharstring()
            .Op(FontCharstring.HMoveTo, 50)
            .Op(FontCharstring.VLineTo, 700, 400, -700));
        Add(600, 'A', new FontCharstring()
            .Numbers(100, 0, -20, 30, 0, 0, 2).Op(FontCharstring.Blend)
            .Op(FontCharstring.RMoveTo)
            .Op(FontCharstring.VLineTo, 700)
            .Numbers(400, 60, -60, 1).Op(FontCharstring.Blend)
            .Op(FontCharstring.HLineTo)
            .Op(FontCharstring.VLineTo, -700));
        Add(500, 'B', new FontCharstring()
            .Op(FontCharstring.HMoveTo, 50)
            .Call(FontCharstring.CallSubr, 0, subroutineCount: 1));
        Add(600, 'C', new FontCharstring()
            .Op(FontCharstring.VsIndex, 1)
            .Op(FontCharstring.RMoveTo, 0, 0)
            .Numbers(300, 600, 0, 100, 2).Op(FontCharstring.Blend)
            .Op(FontCharstring.RLineTo)
            .Numbers(300, -600, 0, -100, 2).Op(FontCharstring.Blend)
            .Op(FontCharstring.RLineTo));
        Add(600, 'D', new FontCharstring()
            .Op(FontCharstring.HMoveTo, 100)
            .Numbers(0, 300, 200, 100, 200, 0)
            .Numbers(0, 0, 20, 10, -10, 0, 30, -10, 40, -20, 0, 0, 6).Op(FontCharstring.Blend)
            .Op(FontCharstring.RRCurveTo)
            .Op(FontCharstring.RLineTo, 0, -400));
        Add(300, ' ', new FontCharstring());
        cff.Apply(font);

        var advances = new FontVariationStore();
        advances.Regions.Add(new FontVariationRegion((0, 1, 1)));
        advances.Regions.Add(new FontVariationRegion((-1, -1, 0)));
        var items = new FontVariationSubtable { Regions = [0, 1] };
        items.Items.AddRange([[0, 0], [40, -40], [80, 0], [0, 0], [0, 0], [0, 0]]);
        advances.Subtables.Add(items);
        font.AdvanceVariations = advances;
        return font;
    }
}
