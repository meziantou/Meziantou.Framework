using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The instances of a variable font: the expected values are computed by hand from the description of the synthetic
/// fonts (<see cref="FontSamples.Variable"/> and <see cref="FontSamples.VariableCharstrings"/>). The same fonts, and
/// real ones, are compared with the reference readers in <c>Conformance/FontConformanceTests</c>.
/// </summary>
public sealed class FontVariationTests
{
    private static Font Variable { get; } = FontTestSupport.Load(FontSamples.Variable());

    private static Font VariableWithoutMetrics { get; } = FontTestSupport.Load(FontSamples.Variable(withAdvanceVariations: false));

    private static Font VariableCharstrings { get; } = FontTestSupport.Load(FontSamples.VariableCharstrings());

    [Fact]
    public void AVariableFontDescribesItsAxesAndItsNamedInstances()
    {
        var font = Variable;
        Assert.Equal(
            ["wght (Weight): 100 to 900, 400 by default", "wdth (Width): 50 to 200, 100 by default"],
            font.VariationAxes.Select(axis => axis.ToString()));
        Assert.All(font.VariationAxes, axis => Assert.False(axis.IsHidden));
        Assert.Equal([new FontVariationSetting("wght", 400), new FontVariationSetting("wdth", 100)], font.Variations);
        Assert.Equal(400, font.Weight);

        Assert.Equal(["Light", "Bold", "Wide"], font.NamedInstances.Select(instance => instance.Name));
        Assert.Equal([null, "MeziantouVariable-Bold", null], font.NamedInstances.Select(instance => instance.PostScriptName));
        Assert.Equal([new FontVariationSetting("wght", 900), new FontVariationSetting("wdth", 100)], font.NamedInstances[1].Settings);
        Assert.Equal("Bold", font.NamedInstances[1].ToString());

        // A font that does not vary has no axis, and is its only instance.
        var boxes = FontTestSupport.Load(FontSamples.Boxes());
        Assert.Empty(boxes.VariationAxes);
        Assert.Empty(boxes.NamedInstances);
        Assert.Empty(boxes.Variations);
        Assert.Same(boxes, boxes.WithVariations(new FontVariationSetting("wght", 700)));

        // An axis the font hides, and one without name: its tag stands for it.
        var builder = FontSamples.Variable();
        builder.Axes[1] = new FontAxisDefinition("TEST", -1, 0, 1, "", IsHidden: true);
        var hidden = FontTestSupport.Load(builder);
        Assert.Equal("TEST (TEST): -1 to 1, 0 by default", hidden.VariationAxes[1].ToString());
        Assert.True(hidden.VariationAxes[1].IsHidden);
    }

    [Fact]
    public void AnInstanceHasAValueForEachAxis()
    {
        var font = Variable;
        var bold = font.WithVariations(new FontVariationSetting("wght", 900));
        Assert.Equal([new FontVariationSetting("wght", 900), new FontVariationSetting("wdth", 100)], bold.Variations);
        Assert.Equal(900, bold.Weight);
        Assert.Same(font.VariationAxes, bold.VariationAxes);
        Assert.Same(font.NamedInstances, bold.NamedInstances);
        Assert.Equal("Meziantou Variable", bold.FamilyName);
        Assert.Equal(font.GlyphCount, bold.GlyphCount);

        // A value outside the range of its axis is brought to the nearest end; a tag the font does not have is
        // ignored; the last value of an axis is the one that is used.
        Assert.Equal((double[])[900, 50], Values(font.WithVariations(new FontVariationSetting("wght", 2000), new FontVariationSetting("wdth", -1e9))));
        Assert.Equal((double[])[400, 100], Values(font.WithVariations(new FontVariationSetting("slnt", -10))));
        Assert.Equal((double[])[700, 100], Values(font.WithVariations(new FontVariationSetting("wght", 300), new FontVariationSetting("wght", 700))));
        Assert.Equal((double[])[650.5, 100], Values(font.WithVariations(new FontVariationSetting("wght", 650.5))));

        // An axis that is not named is at its default, whatever the font the instance is asked to.
        Assert.Equal((double[])[400, 200], Values(bold.WithVariations(new FontVariationSetting("wdth", 200))));
        Assert.Equal((double[])[400, 100], Values(bold.WithVariations()));
        Assert.Equal(400, bold.WithVariations().Weight);

        // A named instance.
        Assert.Equal((double[])[900, 100], Values(font.WithVariations(font.NamedInstances[1])));
        Assert.Equal((double[])[400, 200], Values(bold.WithVariations(font.NamedInstances[2])));
        Assert.Throws<ArgumentNullException>(() => font.WithVariations((FontNamedInstance)null!));

        Assert.Throws<ArgumentNullException>(() => new FontVariationSetting(null!, 1));
        Assert.Throws<ArgumentException>(() => new FontVariationSetting("wgh", 1));
        Assert.Throws<ArgumentException>(() => new FontVariationSetting("wghé", 1));
        Assert.Throws<ArgumentException>(() => new FontVariationSetting("wght", double.NaN));
        Assert.Throws<ArgumentException>(() => new FontVariationSetting("wght", double.PositiveInfinity));
        Assert.Equal("\"wght\" 650.5", new FontVariationSetting("wght", 650.5).ToString());
        Assert.Equal("    ", default(FontVariationSetting).Tag);
        Assert.True(new FontVariationSetting("wght", 1) == new FontVariationSetting("wght", 1));
        Assert.True(new FontVariationSetting("wght", 1) != new FontVariationSetting("wdth", 1));

        static double[] Values(Font font) => [.. font.Variations.Select(setting => setting.Value)];
    }

    [Theory]
    [InlineData("wght=900", "M80,0L80,700L560,700L560,0L80,0Z", 640)]
    [InlineData("wght=100", "M130,0L130,700L470,700L470,0L130,0Z", 560)]

    // The avar table maps the middle of the heavier half of the axis to a quarter, and three quarters to 0.625.
    [InlineData("wght=650", "M95,0L95,700L515,700L515,0L95,0Z", 610)]
    [InlineData("wght=775", "M87.5,0L87.5,700L537.5,700L537.5,0L87.5,0Z", 625)]
    [InlineData("wdth=200", "M100,0L100,700L900,700L900,0L100,0Z", 1000)]
    [InlineData("wdth=50", "M100,0L100,700L300,700L300,0L100,0Z", 400)]
    [InlineData("wdth=150", "M100,0L100,700L700,700L700,0L100,0Z", 800)]

    // The region of the corner applies with the product of what each axis gives: 1, then 0.625 x 0.5.
    [InlineData("wght=900,wdth=200", "M80,0L80,700L1010,700L1010,0L80,0Z", 1090)]
    [InlineData("wght=775,wdth=150", "M87.5,0L87.5,700L753.125,700L753.125,0L87.5,0Z", 841)]

    // The other side of the default: the regions of the heavier weights do not apply.
    [InlineData("wght=250,wdth=75", "M115,0L115,700L385,700L385,0L115,0Z", 480)]
    public void TheDeltasOfEveryPointAreScaledByTheirRegion(string variations, string outline, int advance)
    {
        foreach (var font in (Font[])[Variable, VariableWithoutMetrics])
        {
            var instance = FontTestSupport.WithVariations(font, variations);
            Assert.Equal(outline, FontTestSupport.GetOutline(instance, FontSamples.VariableA, decimals: 3));
            Assert.Equal(advance, instance.Face.GetAdvanceWidth(FontSamples.VariableA));

            // F uses the metrics of A, its first component.
            Assert.Equal(advance, instance.Face.GetAdvanceWidth(FontSamples.VariableF));
        }
    }

    [Fact]
    public void ThePointsWithoutDeltaFollowTheOnesThatHaveOne()
    {
        foreach (var font in (Font[])[Variable, VariableWithoutMetrics])
        {
            // B: two corners have a delta, (50, 500) by (0, +100) and (450, 0) by (+80, 0). The corner (50, 0) is at
            // the smallest coordinates of the two: it takes the horizontal delta of the first one and the vertical
            // delta of the second one, which are zero. The corner (450, 500) takes the two others.
            var bold = FontTestSupport.WithVariations(font, "wght=900");
            Assert.Equal("M50,0L50,600L530,600L530,0L50,0Z", FontTestSupport.GetOutline(bold, FontSamples.VariableB, decimals: 3));
            Assert.Equal(580, bold.Face.GetAdvanceWidth(FontSamples.VariableB));

            // C: (0, 0) stays and (600, 600) moves by (+200, +100). The points between them around the contour are
            // interpolated where their coordinate is between the two, and take the delta of the nearest one elsewhere.
            var wide = FontTestSupport.WithVariations(font, "wdth=200");
            Assert.Equal("M0,0L0,700L400,700L800,700L800,0L400,0L0,0Z", FontTestSupport.GetOutline(wide, FontSamples.VariableC, decimals: 3));
            Assert.Equal(900, wide.Face.GetAdvanceWidth(FontSamples.VariableC));
            var narrow = FontTestSupport.WithVariations(font, "wdth=50");
            Assert.Equal("M0,0L0,600L250,600L500,600L500,0L250,0L0,0Z", FontTestSupport.GetOutline(narrow, FontSamples.VariableC, decimals: 3));
            Assert.Equal(600, narrow.Face.GetAdvanceWidth(FontSamples.VariableC));

            // D: the control point of the curve of the first contour is the only point with a delta: the whole
            // contour moves with it, and the second contour, which has none, stays.
            Assert.Equal(
                "M100,100L100,500Q300,700 500,500L500,100L100,100ZM200,100L400,100L400,300L200,300L200,100Z",
                FontTestSupport.GetOutline(bold, FontSamples.VariableD, decimals: 3));
            Assert.Equal(600, bold.Face.GetAdvanceWidth(FontSamples.VariableD));
        }
    }

    [Theory]

    // The region starts at 0.25, peaks at 0.5 and ends at 1, on the values the avar table gives: 650 is where it starts,
    // 775 is 0.625, three quarters of the way down from the peak, and 900 is where it ends.
    [InlineData("wght=650", 0)]
    [InlineData("wght=775", 30)]
    [InlineData("wght=900", 0)]
    [InlineData("wght=712.5", 30)]
    [InlineData("wght=100", 0)]
    public void ARegionCanStartAwayFromTheDefault(string variations, int rise)
    {
        var instance = FontTestSupport.WithVariations(Variable, variations);
        var expected = string.Create(CultureInfo.InvariantCulture, $"M0,{rise}L0,{600 + rise}L300,{600 + rise}L600,{600 + rise}L600,{rise}L300,{rise}L0,{rise}Z");
        Assert.Equal(0.0, FontTestSupport.GetOutlineDifference(expected, FontTestSupport.GetOutline(instance, FontSamples.VariableC, decimals: 3))!.Value, 0.002);
    }

    [Fact]
    public void TheComponentsOfACompositeGlyphMoveAndVary()
    {
        foreach (var font in (Font[])[Variable, VariableWithoutMetrics])
        {
            // E: A as the instance draws it, and B as the instance draws it, moved by (600, 0) and by the delta of
            // its offset, (+40, +10).
            var bold = FontTestSupport.WithVariations(font, "wght=900");
            Assert.Equal(
                "M80,0L80,700L560,700L560,0L80,0ZM690,10L690,610L1170,610L1170,10L690,10Z",
                FontTestSupport.GetOutline(bold, FontSamples.VariableE, decimals: 3));
            Assert.Equal(1220, bold.Face.GetAdvanceWidth(FontSamples.VariableE));

            // F: B scaled by one half; the delta of its offset, which names its point alone, is not scaled.
            var wide = FontTestSupport.WithVariations(font, "wdth=200");
            Assert.Equal(
                "M100,0L100,700L900,700L900,0L100,0ZM1125,0L1125,250L1325,250L1325,0L1125,0Z",
                FontTestSupport.GetOutline(wide, FontSamples.VariableF, decimals: 3));
            Assert.Equal(1000, wide.Face.GetAdvanceWidth(FontSamples.VariableF));

            // H: deltas of one and two bytes and runs of zeros.
            Assert.Equal(
                "M0,0L0,700L100,700L100,400L400,400L400,700L700,705L700,0L500,-5L100,0L0,0Z",
                FontTestSupport.GetOutline(wide, FontSamples.VariableH, decimals: 3));
            Assert.Equal(1100, wide.Face.GetAdvanceWidth(FontSamples.VariableH));

            // A glyph without outline varies its advance: its only points are the phantom points.
            Assert.Equal("", FontTestSupport.GetOutline(wide, FontSamples.VariableSpace));
            Assert.Equal(600, wide.Face.GetAdvanceWidth(FontSamples.VariableSpace));
            Assert.Equal(450, FontTestSupport.WithVariations(font, "wdth=150").Face.GetAdvanceWidth(FontSamples.VariableSpace));
            Assert.Equal(300, bold.Face.GetAdvanceWidth(FontSamples.VariableSpace));
        }
    }

    [Fact]
    public void AGlyphIsDrawnFromItsOrigin()
    {
        // G: its outline starts at 100 and its left side bearing is 60: the origin is at 40, and the glyph is drawn 40
        // units to the left of its coordinates, as a TrueType rasterizer places it.
        Assert.Equal("M60,0L60,700L460,700L460,0L60,0Z", FontTestSupport.GetOutline(Variable, FontSamples.VariableG));
        Assert.Equal("M60,0L60,700L460,700L460,0L60,0Z", FontTestSupport.GetOutline(FontTestSupport.WithVariations(Variable, "wght=900"), FontSamples.VariableG));

        // In the font without HVAR table, the origin and the advance point of G move together by 20 units at the
        // heaviest weight: the glyph is drawn 20 units further left, and its advance is the same.
        var bold = FontTestSupport.WithVariations(VariableWithoutMetrics, "wght=900");
        Assert.Equal("M40,0L40,700L440,700L440,0L40,0Z", FontTestSupport.GetOutline(bold, FontSamples.VariableG));
        Assert.Equal(600, bold.Face.GetAdvanceWidth(FontSamples.VariableG));

        // A static font does the same; a composite glyph takes the origin of the component whose metrics it uses.
        var builder = FontSamples.Boxes();
        builder.Glyphs[FontSamples.BoxA].LeftSideBearing = 28;
        var boxes = FontTestSupport.Load(builder);
        Assert.Equal("M28,0L28,768L796,768L796,0L28,0Z", FontTestSupport.GetOutline(boxes, FontSamples.BoxA));
        Assert.Equal("M28,0L28,768L796,768L796,0L28,0ZM412,832L412,960L668,960L668,832L412,832Z", FontTestSupport.GetOutline(boxes, FontSamples.CompositeAacute));
        Assert.Equal("M64,0L64,512L448,512L448,0L64,0Z", FontTestSupport.GetOutline(boxes, FontSamples.BoxB));
    }

    [Fact]
    public void TheMetricsOfAnInstanceVary()
    {
        var regular = Variable.Metrics;
        Assert.Equal((800, 200, 0, 500, 700), (regular.Ascent, regular.Descent, regular.LineGap, regular.XHeight, regular.CapHeight));
        Assert.Equal((100, 50, 250, 50), (regular.UnderlinePosition, regular.UnderlineThickness, regular.StrikethroughPosition, regular.StrikethroughThickness));

        var bold = FontTestSupport.WithVariations(Variable, "wght=900").Metrics;
        Assert.Equal((850, 230, 12, 520, 710), (bold.Ascent, bold.Descent, bold.LineGap, bold.XHeight, bold.CapHeight));
        Assert.Equal((120, 80, 265, 75), (bold.UnderlinePosition, bold.UnderlineThickness, bold.StrikethroughPosition, bold.StrikethroughThickness));

        var light = FontTestSupport.WithVariations(Variable, "wght=100").Metrics;
        Assert.Equal((780, 190, 0, 500, 700), (light.Ascent, light.Descent, light.LineGap, light.XHeight, light.CapHeight));
        Assert.Equal((100, 40, 250, 45), (light.UnderlinePosition, light.UnderlineThickness, light.StrikethroughPosition, light.StrikethroughThickness));

        // The width does not change the metrics of this font; the metrics of an instance are scaled like the others.
        Assert.Equal(regular.Ascent, FontTestSupport.WithVariations(Variable, "wdth=200").Metrics.Ascent);
        Assert.Equal(17, FontTestSupport.WithVariations(Variable, "wght=900").GetMetrics(20).Ascent);
    }

    [Theory]
    [InlineData("wght=400", "M100,0L100,700L500,700L500,0L100,0Z", "M50,0L50,500L450,500L450,0L50,0Z", "M0,0L300,600L600,0L0,0Z", 600, 500)]
    [InlineData("wght=900", "M80,0L80,700L540,700L540,0L80,0Z", "M50,0L50,600L530,600L530,0L50,0Z", "M0,0L300,700L600,0L0,0Z", 640, 580)]
    [InlineData("wght=100", "M130,0L130,700L470,700L470,0L130,0Z", "M50,0L50,500L450,500L450,0L50,0Z", "M0,0L300,600L600,0L0,0Z", 560, 500)]
    [InlineData("wght=650", "M90,0L90,700L520,700L520,0L90,0Z", "M50,0L50,550L490,550L490,0L50,0Z", "M0,0L300,650L600,0L0,0Z", 620, 540)]
    [InlineData("wght=250", "M115,0L115,700L485,700L485,0L115,0Z", "M50,0L50,500L450,500L450,0L50,0Z", "M0,0L300,600L600,0L0,0Z", 580, 500)]
    public void TheCharstringsOfACff2TableBlendTheirArguments(string variations, string a, string b, string c, int advanceOfA, int advanceOfB)
    {
        // A blends with the two regions of the first subtable of the store; B, of the second font DICT, with the only
        // region of the second subtable, in a subroutine; C selects the second subtable itself.
        foreach (var format in (int[])[0, 3, 4])
        {
            var font = FontTestSupport.WithVariations(format == 3 ? VariableCharstrings : FontTestSupport.Load(FontSamples.VariableCharstrings(format)), variations);
            Assert.Equal(a, FontTestSupport.GetOutline(font, 1, decimals: 3));
            Assert.Equal(b, FontTestSupport.GetOutline(font, 2, decimals: 3));
            Assert.Equal(c, FontTestSupport.GetOutline(font, 3, decimals: 3));
            Assert.Equal("", FontTestSupport.GetOutline(font, 5));
            Assert.Equal(advanceOfA, font.Face.GetAdvanceWidth(1));
            Assert.Equal(advanceOfB, font.Face.GetAdvanceWidth(2));
            Assert.Equal("M50,0L50,700L450,700L450,0L50,0Z", FontTestSupport.GetOutline(font, 0));
        }
    }

    [Fact]
    public void ABlendReplacesSeveralValuesAtOnce()
    {
        // D: a curve from (100, 0) whose six arguments are blended by one operator. The arguments are (0, 300),
        // (200, 100) and (200, 0); at the heaviest weight they are (0, 320), (190, 130) and (240, 0), and at the
        // lightest (0, 310), (200, 90) and (180, 0).
        Assert.Equal("M100,0C100,300 300,400 500,400L500,0L100,0Z", FontTestSupport.GetOutline(VariableCharstrings, 4));
        Assert.Equal("M100,0C100,320 290,450 530,450L530,50L100,0Z", FontTestSupport.GetOutline(FontTestSupport.WithVariations(VariableCharstrings, "wght=900"), 4));
        Assert.Equal("M100,0C100,310 300,400 480,400L480,0L100,0Z", FontTestSupport.GetOutline(FontTestSupport.WithVariations(VariableCharstrings, "wght=100"), 4));
        Assert.Equal("M100,0C100,305 297.5,412.5 507.5,412.5L507.5,12.5L100,0Z", FontTestSupport.GetOutline(FontTestSupport.WithVariations(VariableCharstrings, "wght=525"), 4, decimals: 3));
    }

    [Fact]
    public void TheFormsOfTheVariationTablesGiveTheSameInstances()
    {
        // The peaks of the regions in each tuple variation instead of once for the table, and 32-bit offsets.
        var builder = FontSamples.Variable();
        builder.ShareVariationTuples = false;
        builder.UseLongVariationOffsets = true;
        AssertSameInstances(FontTestSupport.Load(builder));

        // The advances without a map from the glyphs to the items of the store: the item of a glyph is its index.
        builder = FontSamples.Variable();
        var store = new FontVariationStore();
        store.Regions.AddRange(builder.AdvanceVariations!.Regions);
        var items = new FontVariationSubtable { Regions = [0, 1, 2, 3, 4] };
        items.Items.AddRange([[0, 0, 0, 0, 0], [0, 0, 300, 0, 0], [40, -40, 400, -200, 50], [80, 0, 0, 0, 0], [0, 0, 200, -100, 0], [0, 0, 0, 0, 0], [120, -40, 400, -200, 50], [40, -40, 400, -200, 50], [0, 0, 0, 0, 0], [0, 0, 300, 0, 0]]);
        store.Subtables.Add(items);
        builder.AdvanceVariations = store;
        builder.AdvanceVariationMap = null;
        AssertSameInstances(FontTestSupport.Load(builder));

        // Deltas of 32 and 16 bits, in a column order that makes every delta wide.
        var wide = new FontVariationSubtable { Regions = [0, 1, 2, 3, 4], UseLongDeltas = true };
        wide.Items.AddRange(items.Items);
        wide.Items[0] = [0, 0, 0, 0, 100000];
        store.Subtables[0] = wide;
        AssertSameInstances(FontTestSupport.Load(builder));
        Assert.Equal(100500, FontTestSupport.WithVariations(FontTestSupport.Load(builder), "wght=900,wdth=200").Face.GetAdvanceWidth(0));

        // A map with a 32-bit count and entries of four bytes; a glyph beyond the map uses its last entry.
        builder = FontSamples.Variable();
        builder.AdvanceVariationMap = FontVariationWriter.WriteIndexMap([(0, 0), (1, 0), (0, 1), (0, 2), (0, 3), (0, 0), (0, 4), (0, 1), (0, 0), (1, 0)], format: 1, entrySize: 4);
        AssertSameInstances(FontTestSupport.Load(builder));
        builder.AdvanceVariationMap = FontVariationWriter.WriteIndexMap([(0, 0), (1, 0), (0, 1)], entrySize: 3);
        var font = FontTestSupport.WithVariations(FontTestSupport.Load(builder), "wght=900");
        Assert.Equal((640, 540, 740, 640), (font.Face.GetAdvanceWidth(FontSamples.VariableA), font.Face.GetAdvanceWidth(FontSamples.VariableB), font.Face.GetAdvanceWidth(FontSamples.VariableC), font.Face.GetAdvanceWidth(FontSamples.VariableD)));

        static void AssertSameInstances(Font font)
        {
            foreach (var variations in (string[])["wght=900", "wght=100,wdth=50", "wght=775,wdth=150", "wdth=200"])
            {
                var expected = FontTestSupport.WithVariations(Variable, variations);
                var actual = FontTestSupport.WithVariations(font, variations);
                for (var glyph = 0; glyph < expected.GlyphCount; glyph++)
                {
                    Assert.Equal(FontTestSupport.GetOutline(expected, glyph, decimals: 6), FontTestSupport.GetOutline(actual, glyph, decimals: 6));
                    if (glyph != 0)
                    {
                        Assert.Equal(expected.Face.GetAdvanceWidth(glyph), actual.Face.GetAdvanceWidth(glyph));
                    }
                }
            }
        }
    }

    [Fact]
    public void DeltasCanBeWiderThanSixteenBits()
    {
        // A delta of 32 bits for the right side of A, which the format of the table allows and the points of an outline
        // do not: the glyph is refused where its points leave the coordinates an outline can have.
        var builder = FontSamples.Variable();
        var a = builder.Glyphs[FontSamples.VariableA];
        a.Variations[2] = new FontTupleVariation { Peak = [0, 1], Deltas = [(0, 0), (0, 0), (100000, 0), (100000, 0), (0, 0), (100000, 0), (0, 0), (0, 0)] };
        builder.AdvanceVariations = null;
        var font = FontTestSupport.Load(builder);
        var wide = FontTestSupport.WithVariations(font, "wdth=200");
        Assert.Equal("M100,0L100,700L100500,700L100500,0L100,0Z", FontTestSupport.GetOutline(wide, FontSamples.VariableA));
        Assert.Equal(100600, wide.Face.GetAdvanceWidth(FontSamples.VariableA));

        a.Variations[2] = new FontTupleVariation { Peak = [0, 1], Deltas = [(0, 0), (0, 0), (100000000, 0), (100000000, 0), (0, 0), (0, 0), (0, 0), (0, 0)] };
        wide = FontTestSupport.WithVariations(FontTestSupport.Load(builder), "wdth=200");
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(wide, FontSamples.VariableA));
    }

    [Fact]
    public void MalformedVariationTablesAreRejectedOrLeftOut()
    {
        var tables = FontSamples.Variable().BuildTables();
        var heavy = "wght=900";
        var defaultA = "M100,0L100,700L500,700L500,0L100,0Z";

        // A glyph variation table for another number of axes cannot apply: the outlines do not vary, the advances do.
        var font = Edit("gvar", table => table[5] = 3);
        Assert.Equal(defaultA, FontTestSupport.GetOutline(FontTestSupport.WithVariations(font, heavy), FontSamples.VariableA));
        Assert.Equal(640, FontTestSupport.WithVariations(font, heavy).Face.GetAdvanceWidth(FontSamples.VariableA));

        // A tuple variation that refers to a shared tuple the table does not have. The default instance does not read
        // the variations.
        font = Edit("gvar", table => table[7] = 0);
        Assert.Equal(defaultA, FontTestSupport.GetOutline(font, FontSamples.VariableA));
        var exception = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(FontTestSupport.WithVariations(font, heavy), FontSamples.VariableA));
        Assert.Contains("'gvar'", exception.Message, StringComparison.Ordinal);

        // The variations of a glyph outside the table, and a table cut inside the deltas of a glyph.
        font = Load(tables, "gvar", tables["gvar"][..80]);
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(FontTestSupport.WithVariations(font, heavy), FontSamples.VariableH));
        Assert.Throws<InvalidImageContentException>(() => TextLayout.Create("H", FontTestSupport.WithVariations(font, "wdth=200"), 20));

        // An advance variation table whose store is not where it says is left out: the advances then vary with the
        // phantom points of the glyphs, as in a font without this table.
        font = Edit("HVAR", table => table[6] = 0x7F);
        Assert.Equal(640, FontTestSupport.WithVariations(font, heavy).Face.GetAdvanceWidth(FontSamples.VariableA));
        Assert.Equal(841, FontTestSupport.WithVariations(font, "wght=775,wdth=150").Face.GetAdvanceWidth(FontSamples.VariableF));
        Assert.Equal("M80,0L80,700L560,700L560,0L80,0Z", FontTestSupport.GetOutline(FontTestSupport.WithVariations(font, heavy), FontSamples.VariableA));

        // A metrics variation table of another version is not read.
        font = Edit("MVAR", table => table[1] = 2);
        Assert.Equal(800, FontTestSupport.WithVariations(font, heavy).Metrics.Ascent);

        // An axis table of another version, or without axis: the font does not vary.
        font = Edit("fvar", table => table[1] = 2);
        Assert.Empty(font.VariationAxes);
        Assert.Same(font, font.WithVariations(new FontVariationSetting("wght", 900)));
        font = Edit("fvar", table => table[9] = 0);
        Assert.Empty(font.VariationAxes);

        // Axis records shorter than their fields, instance records shorter than their coordinates, an axis whose tag is
        // not text, and a table cut inside its instances.
        Assert.Throws<InvalidImageContentException>(() => Edit("fvar", table => table[11] = 16));
        Assert.Throws<InvalidImageContentException>(() => Edit("fvar", table => table[15] = 8));
        Assert.Throws<InvalidImageContentException>(() => Edit("fvar", table => table[16] = 0x07));
        Assert.Throws<InvalidImageContentException>(() => Load(tables, "fvar", tables["fvar"][..^6]));

        // An axis whose default is outside its range: the range is widened to it.
        font = Edit("fvar", table => (table[20], table[21]) = (0x01, 0xF4));
        Assert.Equal((400, 400, 900), (font.VariationAxes[0].Minimum, font.VariationAxes[0].Default, font.VariationAxes[0].Maximum));

        // The work of the tuple variations is charged to the complexity of the glyph: A has four points, five tuple
        // variations of two axes, and eight points with its phantom points.
        var limited = new FontLoadOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxGlyphComplexity = 20 } } };
        font = Font.Load(FontBuilder.WriteSfnt(tables), limited);
        Assert.Equal(defaultA, FontTestSupport.GetOutline(font, FontSamples.VariableA));
        var limit = Assert.Throws<ImageResourceLimitException>(() => FontTestSupport.GetOutline(FontTestSupport.WithVariations(font, heavy), FontSamples.VariableA));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, limit.Kind);

        Font Edit(string tag, Action<byte[]> edit)
        {
            var table = (byte[])tables[tag].Clone();
            edit(table);
            return Load(tables, tag, table);
        }

        static Font Load(SortedDictionary<string, byte[]> tables, string tag, byte[] table)
        {
            var copy = new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { [tag] = table };
            return Font.Load(FontBuilder.WriteSfnt(copy));
        }
    }

    [Fact]
    public void MalformedCff2TablesAreRejected()
    {
        // A blend that takes more values than the stack holds, and a blend of a number that is not a count.
        Assert.Throws<InvalidImageContentException>(() => Decode(new FontCharstring().Numbers(1, 2, 5).Op(FontCharstring.Blend).Op(FontCharstring.RMoveTo)));
        Assert.Throws<InvalidImageContentException>(() => Decode(new FontCharstring().Numbers(1, 2, -1).Op(FontCharstring.Blend).Op(FontCharstring.RMoveTo)));
        Assert.Throws<InvalidImageContentException>(() => Decode(new FontCharstring().Op(FontCharstring.Blend)));
        Assert.Throws<InvalidImageContentException>(() => Decode(new FontCharstring().Op(FontCharstring.VsIndex)));

        // A subtable of the store that does not exist has no region: a blend only removes its count.
        Assert.Equal("M10,20L40,60L10,20Z", Decode(new FontCharstring().Op(FontCharstring.VsIndex, 7).Numbers(10, 20, 2).Op(FontCharstring.Blend).Op(FontCharstring.RMoveTo).Op(FontCharstring.RLineTo, 30, 40)));

        // The operators of a CFF2 table are not those of a CFF table.
        var charstrings = new FontCffWriter();
        charstrings.CharStrings.Add(new FontCharstring().Numbers(1, 2, 0).Op(FontCharstring.Blend).Op(FontCharstring.RMoveTo).Op(FontCharstring.EndChar).ToArray());
        var builder = new FontBuilder();
        builder.AddGlyph(FontGlyph.Empty(500));
        charstrings.Apply(builder);
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(FontTestSupport.Load(builder), 0));

        // A table without font DICT, and a version that is not known.
        var tables = FontSamples.VariableCharstrings().BuildTables();
        var table = (byte[])tables["CFF2"].Clone();
        table[0] = 3;
        Assert.Throws<InvalidImageContentException>(() => Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["CFF2"] = table }, 0x4F54544F)));
        Assert.Throws<InvalidImageContentException>(() => Font.Load(FontBuilder.WriteSfnt(new SortedDictionary<string, byte[]>(tables, StringComparer.Ordinal) { ["CFF2"] = tables["CFF2"][..40] }, 0x4F54544F)));

        static string Decode(FontCharstring charstring)
        {
            var builder = new FontBuilder { UnitsPerEm = 1000 };
            builder.Axes.Add(new FontAxisDefinition("wght", 100, 400, 900, "Weight"));
            var store = new FontVariationStore();
            store.Regions.Add(new FontVariationRegion((0, 1, 1)));
            store.Subtables.Add(new FontVariationSubtable { Regions = [0] });
            var cff2 = new FontCff2Writer { VariationStore = store };
            builder.AddGlyph(FontGlyph.Empty(500));
            cff2.CharStrings.Add(charstring.ToArray());
            cff2.Apply(builder);
            return FontTestSupport.GetOutline(FontTestSupport.WithVariations(FontTestSupport.Load(builder), "wght=900"), 0);
        }
    }

    [Fact]
    public void ATextIsLaidOutAndDrawnWithAnInstance()
    {
        // "AB" at 20 pixels: A advances by 600 units and B by 500; at the heaviest weight, by 640 and 580.
        var regular = TextLayout.Create("AB", Variable, 20);
        var bold = TextLayout.Create("AB", FontTestSupport.WithVariations(Variable, "wght=900"), 20);
        Assert.Equal(22, regular.Width);
        Assert.Equal(24.4, bold.Width, 1e-9);

        // The line is as tall as the ascent and the descent of the instance: 800 and 200 units, then 850 and 230 with
        // a line gap of 12.
        Assert.Equal(20, regular.Height);
        Assert.Equal(21.84, bold.Height, 1e-9);

        // The ink: A from 100 to 500 and B from 650 to 1050 units, 700 units tall; then A from 80 to 560 and B from 690
        // to 1170, where B is 600 units tall.
        Assert.Equal(RectangleD.FromLTRB(2, 2, 21, 16), regular.InkBounds);
        var ink = bold.InkBounds;
        Assert.Equal(1.6, ink.Left, 1e-9);
        Assert.Equal(23.4, ink.Right, 1e-9);

        // Two instances of a font in one text are two fonts.
        var rich = new RichTextBuilder()
            .Append("A", new TextStyle(Variable, 20))
            .Append("A", new TextStyle(FontTestSupport.WithVariations(Variable, "wdth=200"), 20))
            .Build();
        Assert.Equal(32, TextLayout.Create(rich).Width);

        using var image = new Image<Gray8>(40, 30, new Gray8(0));
        image.Draw(bold, static (canvas, layout) => canvas.FillText(new Rgba32(255, 255, 255), layout, new PointD(0.4, 0)));

        // A is drawn from x = 0.4 + 1.6 = 2 to 0.4 + 11.2 = 11.6: the pixel 2 is covered, the pixel 11 by six tenths.
        var baseline = (int)Math.Floor(bold.Baseline);
        Assert.Equal(new Gray8(255), image.Frames[0][2, baseline - 2]);
        Assert.Equal(new Gray8(153), image.Frames[0][11, baseline - 2]);
        Assert.Equal(new Gray8(0), image.Frames[0][1, baseline - 2]);
    }
}
