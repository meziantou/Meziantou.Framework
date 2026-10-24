using System.Buffers.Binary;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Font loading and font tables, on fonts built by the test (Common/FontBuilder.cs) whose content is known by construction.</summary>
public sealed class FontTests
{
    [Fact]
    public void AFontExposesItsNamesAndItsStyle()
    {
        var font = FontTestSupport.Load(FontSamples.Boxes());

        Assert.Equal("Meziantou Boxes", font.FamilyName);
        Assert.Equal("Regular", font.SubfamilyName);
        Assert.Equal("MeziantouBoxes-Regular", font.PostScriptName);
        Assert.Equal(400, font.Weight);
        Assert.False(font.IsItalic);
        Assert.False(font.IsMonospaced);
        Assert.Equal(1024, font.UnitsPerEm);
        Assert.Equal(13, font.GlyphCount);

        var styled = FontSamples.Boxes();
        styled.WeightClass = 700;
        styled.IsBold = true;
        styled.IsItalic = true;
        styled.IsMonospaced = true;
        styled.PostScriptName = null;
        var bold = FontTestSupport.Load(styled);
        Assert.Equal(700, bold.Weight);
        Assert.True(bold.IsItalic);
        Assert.True(bold.IsMonospaced);
        Assert.Null(bold.PostScriptName);
    }

    [Fact]
    public void TheTypographicNamesReplaceTheLegacyNames()
    {
        var builder = FontSamples.Boxes();
        builder.FamilyName = "Meziantou Boxes Light";
        builder.SubfamilyName = "Regular";
        builder.TypographicFamilyName = "Meziantou Boxes";
        builder.TypographicSubfamilyName = "Light";

        var font = FontTestSupport.Load(builder);

        Assert.Equal("Meziantou Boxes", font.FamilyName);
        Assert.Equal("Light", font.SubfamilyName);
    }

    [Fact]
    public void TheNamesAreReadFromTheMacintoshPlatformWhenItIsTheOnlyOne()
    {
        var builder = FontSamples.Boxes();
        builder.OmitWindowsNames = true;
        Assert.Equal("Meziantou Boxes", FontTestSupport.Load(builder).FamilyName);

        // With both platforms, the Windows names win; here they are the same.
        builder.OmitWindowsNames = false;
        builder.WriteMacintoshNames = true;
        Assert.Equal("Meziantou Boxes", FontTestSupport.Load(builder).FamilyName);

        // A font without name table has empty names.
        builder.OmittedTables.Add("name");
        var unnamed = FontTestSupport.Load(builder);
        Assert.Equal("", unnamed.FamilyName);
        Assert.Equal("", unnamed.SubfamilyName);
        Assert.Null(unnamed.PostScriptName);
    }

    [Fact]
    public void MacintoshNamesAreDecodedFromMacRoman()
    {
        // A few characters of the upper half, from the mapping the Unicode Consortium publishes (MAC/ROMAN.TXT):
        // 0x8E is e acute, 0xDB the euro sign, 0xCA the no-break space, 0xF0 the Apple logo (a private use character).
        Assert.Equal("Caf\u00E9 \u20AC\u00A0\uF8FF", NameTable.DecodeMacintoshRoman([(byte)'C', (byte)'a', (byte)'f', 0x8E, (byte)' ', 0xDB, 0xCA, 0xF0]));
        Assert.Equal("", NameTable.DecodeMacintoshRoman([]));

        // Every byte, against the code page 10000 of the framework. It differs from the mapping Apple publishes for one
        // byte, 0xBD: the ohm sign (U+2126) where Apple has the Greek capital omega (U+03A9), its canonical equivalent.
        // The library follows Apple, as Python's mac_roman codec does for the 256 bytes.
        var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var framework = CodePagesEncodingProvider.Instance.GetEncoding(10000)!.GetString(bytes);
        var decoded = NameTable.DecodeMacintoshRoman(bytes);
        var differences = Enumerable.Range(0, 256).Where(value => framework[value] != decoded[value]).Select(value => $"{value:X2}:{(int)decoded[value]:X4}/{(int)framework[value]:X4}").ToArray();
        Assert.Equal(new[] { "BD:03A9/2126" }, differences);

        // Through a font: the builder writes its Macintosh names in ASCII.
        var builder = FontSamples.Boxes();
        builder.OmitWindowsNames = true;
        Assert.Equal("MeziantouBoxes-Regular", FontTestSupport.Load(builder).PostScriptName);
    }

    [Fact]
    public void ANameWhoseStringIsOutsideTheTableIsIgnored()
    {
        // The Windows name starts outside the table: the name of the Unicode platform is used instead.
        var table = WriteNameTable((0, 3, 0, 1, "Unicode"), (3, 1, 0x0409, 1, "Windows"), (3, 1, 0x0409, 4, "Full"));
        Assert.Equal("Windows", NameTable.Find(table, NameTable.FamilyName));
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6 + 12 + 10), 0xFFFF);
        Assert.Equal("Unicode", NameTable.Find(table, NameTable.FamilyName));
        var names = new List<string>();
        NameTable.FindAll(table, NameTable.FamilyName, names);
        Assert.Equal(["Unicode"], names);

        // The only full name ends outside the table: the font has none.
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(6 + 24 + 8), 0x0100);
        Assert.Null(NameTable.Find(table, NameTable.FullName));
        names.Clear();
        NameTable.FindAll(table, NameTable.FullName, names);
        Assert.Empty(names);

        // Through a variable font (a fuzzing finding): the names of an axis, of an instance and of the family are
        // outside the table. The font is still loaded, without these names.
        var font = Font.Load(MoveNamesOutsideTheTable(FontSamples.Variable().Build(), "Weight", "Bold", "MeziantouVariable-Bold", "Meziantou Variable"));
        Assert.Equal("", font.FamilyName);
        Assert.Equal(["wght (wght): 100 to 900, 400 by default", "wdth (Width): 50 to 200, 100 by default"], font.VariationAxes.Select(axis => axis.ToString()));
        Assert.Equal(["Light", "", "Wide"], font.NamedInstances.Select(instance => instance.Name));
        Assert.All(font.NamedInstances, instance => Assert.Null(instance.PostScriptName));
    }

    [Fact]
    public void TheMetricsAreInFontUnitsAndScaleWithTheSize()
    {
        var font = FontTestSupport.Load(FontSamples.Boxes());

        var metrics = font.Metrics;
        Assert.Equal(832, metrics.Ascent);
        Assert.Equal(192, metrics.Descent);
        Assert.Equal(0, metrics.LineGap);
        Assert.Equal(1024, metrics.LineHeight);
        Assert.Equal(768, metrics.CapHeight);
        Assert.Equal(512, metrics.XHeight);
        Assert.Equal(128, metrics.UnderlinePosition);
        Assert.Equal(64, metrics.UnderlineThickness);
        Assert.Equal(256, metrics.StrikethroughPosition);
        Assert.Equal(64, metrics.StrikethroughThickness);

        // 1024 units per em at 16 pixels: 64 units per pixel.
        var scaled = font.GetMetrics(16);
        Assert.Equal(13, scaled.Ascent);
        Assert.Equal(3, scaled.Descent);
        Assert.Equal(16, scaled.LineHeight);
        Assert.Equal(12, scaled.CapHeight);
        Assert.Equal(2, scaled.UnderlinePosition);
        Assert.Equal(1, scaled.UnderlineThickness);
        Assert.Equal(4, scaled.StrikethroughPosition);

        Assert.Equal(metrics, font.GetMetrics(1024));
        Assert.True(metrics != scaled);
        Assert.False(metrics.Equals((object)scaled));
        Assert.Equal(metrics.GetHashCode(), font.GetMetrics(1024).GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(65536.5)]
    public void TheSizeOfAFontIsValidated(double size)
    {
        var font = FontTestSupport.Load(FontSamples.Boxes());

        Assert.Throws<ArgumentOutOfRangeException>(nameof(size), () => font.GetMetrics(size));
    }

    [Fact]
    public void TheBaselinesOfAScriptComeFromTheBaseTable()
    {
        // The values of the reference shaper (hb-info --list-baselines): the horizontal axis gives heights above the
        // baseline, the vertical axis distances from the left of the glyphs. The font gives no top of the
        // ideographic em box: the middle of the box is half an em above its bottom.
        var fonts = FixtureRoot.GetDirectory() / "fonts";
        var face = Font.Load(fonts / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf").Face;
        Assert.Equal((0, -74, 834, -120), GetBaselines(face, isVertical: false, UnicodeScript.Han));
        Assert.Equal((0, -74, 834, -120), GetBaselines(face, isVertical: false, UnicodeScript.Latin));
        Assert.Equal((120, 46, 954, 0), GetBaselines(face, isVertical: true, UnicodeScript.Han));
        Assert.False(face.TryGetBaseline(isVertical: false, GetScripts(UnicodeScript.Han), BaseTable.IdeographicTop, out _));
        Assert.True(face.TryGetIdeographicCenter(isVertical: false, GetScripts(UnicodeScript.Latin), out var center));
        Assert.Equal(380, center);
        Assert.True(face.TryGetIdeographicCenter(isVertical: true, GetScripts(UnicodeScript.Han), out center));
        Assert.Equal(500, center);

        // An instance of a variable font varies the baselines by the deltas of the variation store of the table
        var variable = Font.Load(fonts / "noto-sans-cjk-variable" / "NotoSansCJKjp-Variable.subset.otf");
        foreach (var (variations, horizontal, vertical) in ((string, (int, int, int, int), (int, int, int, int))[])
            [
                ("wght=100", (0, -67, 827, -120), (120, 53, 947, 0)),
                ("wght=650", (0, -87, 847, -120), (120, 33, 967, 0)),
                ("wght=900", (0, -94, 854, -120), (120, 26, 974, 0)),
            ])
        {
            var instance = FontTestSupport.WithVariations(variable, variations).Face;
            Assert.Equal(horizontal, GetBaselines(instance, isVertical: false, UnicodeScript.Han));
            Assert.Equal(vertical, GetBaselines(instance, isVertical: true, UnicodeScript.Han));
        }

        // A font without the table has no baselines
        var boxes = FontTestSupport.Load(FontSamples.Boxes()).Face;
        Assert.False(boxes.TryGetBaseline(isVertical: false, GetScripts(UnicodeScript.Latin), BaseTable.Roman, out _));
        Assert.False(boxes.TryGetIdeographicCenter(isVertical: false, GetScripts(UnicodeScript.Latin), out _));

        static (int Roman, int FaceBottom, int FaceTop, int Bottom) GetBaselines(FontFace face, bool isVertical, UnicodeScript script)
        {
            var scripts = GetScripts(script);
            Assert.True(face.TryGetBaseline(isVertical, scripts, BaseTable.Roman, out var roman));
            Assert.True(face.TryGetBaseline(isVertical, scripts, ShapingFeature.ToTag("icfb"), out var faceBottom));
            Assert.True(face.TryGetBaseline(isVertical, scripts, ShapingFeature.ToTag("icft"), out var faceTop));
            Assert.True(face.TryGetBaseline(isVertical, scripts, BaseTable.IdeographicBottom, out var bottom));
            return (roman, faceBottom, faceTop, bottom);
        }

        static uint[] GetScripts(UnicodeScript script)
        {
            var tags = new uint[OpenTypeScript.MaxTagCount];
            return tags[..OpenTypeScript.GetTags(script, tags)];
        }
    }

    [Fact]
    public void TheVerticalMetricsComeFromTheTableTheFontDesignates()
    {
        FontBuilder Create()
        {
            var builder = FontSamples.Boxes();
            builder.Ascender = 900;
            builder.Descender = -300;
            builder.LineGap = 10;
            builder.TypoAscender = 700;
            builder.TypoDescender = -100;
            builder.TypoLineGap = 20;
            builder.WinAscent = 1000;
            builder.WinDescent = 400;
            return builder;
        }

        // The horizontal header by default.
        var metrics = FontTestSupport.Load(Create()).Metrics;
        Assert.Equal((900, 300, 10), (metrics.Ascent, metrics.Descent, metrics.LineGap));

        // The typographic values when the font asks for them.
        var typographic = Create();
        typographic.UseTypoMetrics = true;
        metrics = FontTestSupport.Load(typographic).Metrics;
        Assert.Equal((700, 100, 20), (metrics.Ascent, metrics.Descent, metrics.LineGap));

        // The typographic values when the horizontal header is empty.
        var withoutHeader = Create();
        withoutHeader.Ascender = 0;
        withoutHeader.Descender = 0;
        metrics = FontTestSupport.Load(withoutHeader).Metrics;
        Assert.Equal((700, 100, 20), (metrics.Ascent, metrics.Descent, metrics.LineGap));

        // The Windows values when nothing else is declared.
        var windowsOnly = Create();
        windowsOnly.Ascender = 0;
        windowsOnly.Descender = 0;
        windowsOnly.TypoAscender = 0;
        windowsOnly.TypoDescender = 0;
        metrics = FontTestSupport.Load(windowsOnly).Metrics;
        Assert.Equal((1000, 400, 0), (metrics.Ascent, metrics.Descent, metrics.LineGap));
    }

    [Fact]
    public void AFontWithoutOs2OrPostTableHasDefaultLines()
    {
        var builder = FontSamples.Boxes();
        builder.OmitOs2 = true;
        builder.OmittedTables.Add("post");
        builder.IsBold = true;

        var font = FontTestSupport.Load(builder);

        // The weight and the style then come from the font header; the lines are fractions of the em (1024 units).
        Assert.Equal(700, font.Weight);
        var metrics = font.Metrics;
        Assert.Equal(832, metrics.Ascent);
        Assert.Equal(192, metrics.Descent);
        Assert.Equal(0, metrics.CapHeight);
        Assert.Equal(0, metrics.XHeight);
        Assert.Equal(128, metrics.UnderlinePosition);
        Assert.Equal(64, metrics.UnderlineThickness);
        Assert.Equal(320, metrics.StrikethroughPosition);
        Assert.Equal(64, metrics.StrikethroughThickness);
    }

    [Theory]
    [InlineData((int)FontCmapFormats.Format4, false)]
    [InlineData((int)FontCmapFormats.Format4, true)]
    [InlineData((int)FontCmapFormats.Format12, false)]
    [InlineData((int)(FontCmapFormats.Format4 | FontCmapFormats.Format12), false)]
    [InlineData((int)(FontCmapFormats.MacintoshFormat6 | FontCmapFormats.Format4), false)]
    public void TheCharacterMapIsReadFromEveryFormat(int formats, bool useGlyphIdArray)
    {
        // The formats are an internal enumeration of the font builder: the theory takes its value.
        var builder = FontSamples.Boxes();
        builder.CmapFormats = (FontCmapFormats)formats;
        builder.Cmap4UsesGlyphIdArray = useGlyphIdArray;
        var font = FontTestSupport.Load(builder);

        foreach (var (codePoint, glyph) in builder.CharacterMap)
        {
            Assert.Equal(glyph, font.Face.MapCodePoint(codePoint));
            Assert.True(font.ContainsGlyph(new Rune(codePoint)));
        }

        // Characters around the mapped ones, and outside the Basic Multilingual Plane.
        foreach (var codePoint in (int[])[0, 0x1F, 0x21, 0x40, 0x47, 0x62, 0x9F, 0xA1, 0xC0, 0xC2, 0x300, 0x302, 0x200F, 0x2011, 0xFFFF, 0x10000, 0x1F600, 0x10FFFF])
        {
            Assert.Equal(0, font.Face.MapCodePoint(codePoint));
            Assert.False(font.ContainsGlyph(new Rune(codePoint)));
        }

        Assert.Equal(0, font.Face.MapCodePoint(-1));
        Assert.Equal(0, font.Face.MapCodePoint(0x110000));
    }

    [Fact]
    public void ACharacterMapOfTheFullRepertoireMapsSupplementaryCharacters()
    {
        var builder = FontSamples.Boxes();
        builder.CmapFormats = FontCmapFormats.Format4 | FontCmapFormats.Format12;
        builder.CharacterMap[0x1F600] = FontSamples.BoxA;
        builder.CharacterMap[0x1F601] = FontSamples.BoxB;
        builder.CharacterMap[0x10FFFF] = FontSamples.BoxC;
        var font = FontTestSupport.Load(builder);

        Assert.Equal(FontSamples.BoxA, font.Face.MapCodePoint(0x1F600));
        Assert.Equal(FontSamples.BoxB, font.Face.MapCodePoint(0x1F601));
        Assert.Equal(FontSamples.BoxC, font.Face.MapCodePoint(0x10FFFF));
        Assert.Equal(0, font.Face.MapCodePoint(0x1F602));
        Assert.Equal(FontSamples.BoxA, font.Face.MapCodePoint('A'));
    }

    [Fact]
    public void AManyToOneCharacterMapGivesARangeOneGlyph()
    {
        // Format 13: a "last resort" map, where every character of a range has the same glyph.
        var builder = FontSamples.Boxes();
        builder.CmapFormats = FontCmapFormats.Format13;
        builder.CharacterMap.Clear();
        for (var codePoint = 0x4E00; codePoint <= 0x4E0F; codePoint++)
        {
            builder.CharacterMap[codePoint] = FontSamples.BoxA;
        }

        builder.CharacterMap[0x4E10] = FontSamples.BoxB;
        var font = FontTestSupport.Load(builder);

        Assert.Equal(FontSamples.BoxA, font.Face.MapCodePoint(0x4E00));
        Assert.Equal(FontSamples.BoxA, font.Face.MapCodePoint(0x4E0F));
        Assert.Equal(FontSamples.BoxB, font.Face.MapCodePoint(0x4E10));
        Assert.Equal(0, font.Face.MapCodePoint(0x4E11));
    }

    [Fact]
    public void AMacintoshCharacterMapOnlyMapsAscii()
    {
        var builder = FontSamples.Boxes();
        builder.CmapFormats = FontCmapFormats.MacintoshFormat6;
        var font = FontTestSupport.Load(builder);

        Assert.Equal(FontSamples.BoxA, font.Face.MapCodePoint('A'));
        Assert.Equal(FontSamples.SpaceGlyph, font.Face.MapCodePoint(' '));

        // 0xA0 and 0xC1 are other characters in MacRoman: they are not mapped through this subtable.
        Assert.Equal(0, font.Face.MapCodePoint(0xA0));
        Assert.Equal(0, font.Face.MapCodePoint(0xC1));
    }

    [Fact]
    public void VariationSequencesAreReadFromTheFormat14Subtable()
    {
        var builder = FontSamples.Boxes();
        builder.CmapFormats = FontCmapFormats.Format4 | FontCmapFormats.Format14;
        builder.VariationSequences[(0xFE00, 'A')] = FontSamples.BoxB;
        builder.VariationSequences[(0xFE00, 'B')] = -1;
        builder.VariationSequences[(0xFE00, 'C')] = -1;
        builder.VariationSequences[(0xFE0F, 'A')] = -1;
        builder.VariationSequences[(0xE0100, 'a')] = FontSamples.BoxC;
        var font = FontTestSupport.Load(builder);

        Assert.Equal(CmapVariation.Glyph, font.Face.MapVariation('A', 0xFE00, out var glyph));
        Assert.Equal(FontSamples.BoxB, glyph);
        Assert.Equal(CmapVariation.UseDefault, font.Face.MapVariation('B', 0xFE00, out glyph));
        Assert.Equal(0, glyph);
        Assert.Equal(CmapVariation.UseDefault, font.Face.MapVariation('C', 0xFE00, out _));
        Assert.Equal(CmapVariation.NotFound, font.Face.MapVariation('D', 0xFE00, out _));
        Assert.Equal(CmapVariation.UseDefault, font.Face.MapVariation('A', 0xFE0F, out _));
        Assert.Equal(CmapVariation.NotFound, font.Face.MapVariation('B', 0xFE0F, out _));
        Assert.Equal(CmapVariation.Glyph, font.Face.MapVariation('a', 0xE0100, out glyph));
        Assert.Equal(FontSamples.BoxC, glyph);
        Assert.Equal(CmapVariation.NotFound, font.Face.MapVariation('a', 0xE0101, out _));
        Assert.Equal(CmapVariation.NotFound, font.Face.MapVariation(0x110000, 0xFE00, out _));

        // A font without the subtable supports no sequence.
        var plain = FontTestSupport.Load(FontSamples.Boxes());
        Assert.Equal(CmapVariation.NotFound, plain.Face.MapVariation('A', 0xFE00, out _));
    }

    [Fact]
    public void TheLegacyKerningTableGivesPairAdjustments()
    {
        var builder = FontSamples.Boxes();
        var font = FontTestSupport.Load(builder);
        Assert.True(font.Face.HasLegacyKerning);
        Assert.Equal(-128, font.Face.GetLegacyKerning(FontSamples.BoxA, FontSamples.BoxB));
        Assert.Equal(64, font.Face.GetLegacyKerning(FontSamples.BoxB, FontSamples.BoxA));
        Assert.Equal(0, font.Face.GetLegacyKerning(FontSamples.BoxA, FontSamples.BoxA));
        Assert.Equal(0, font.Face.GetLegacyKerning(FontSamples.BoxA, 500));
        Assert.Equal(0, font.Face.GetLegacyKerning(-1, FontSamples.BoxB));
        Assert.False(FontTestSupport.Load(FontSamples.Curves()).Face.HasLegacyKerning);

        // Many pairs: every one is found by the search.
        builder = FontSamples.Boxes();
        builder.KerningPairs.Clear();
        for (var left = 0; left < 13; left++)
        {
            for (var right = 0; right < 13; right++)
            {
                builder.KerningPairs[(left, right)] = (left * 13) + right - 80;
            }
        }

        font = FontTestSupport.Load(builder);
        for (var left = 0; left < 13; left++)
        {
            for (var right = 0; right < 13; right++)
            {
                Assert.Equal((left * 13) + right - 80, font.Face.GetLegacyKerning(left, right));
            }
        }
    }

    [Fact]
    public void OnlyTheHorizontalPairsOfTheKerningTableAreUsed()
    {
        // A subtable: version, length, coverage (format in the high byte), then for the format 0 the number of pairs,
        // three search fields, and the pairs: left glyph, right glyph, value.
        static byte[] Subtable(int coverage, params (int Left, int Right, int Value)[] pairs)
        {
            var writer = new FontWriter();
            writer.UInt16(0);
            writer.UInt16((14 + (pairs.Length * 6)) & 0xFFFF);
            writer.UInt16(coverage);
            writer.UInt16(pairs.Length);
            writer.Zeros(6);
            foreach (var (left, right, value) in pairs)
            {
                writer.UInt16(left);
                writer.UInt16(right);
                writer.Int16(value);
            }

            return writer.ToArray();
        }

        static Font Load(params byte[][] subtables)
        {
            var builder = FontSamples.Boxes();
            builder.RawTables["kern"] = [0, 0, (byte)(subtables.Length >> 8), (byte)subtables.Length, .. subtables.SelectMany(subtable => subtable)];
            return FontTestSupport.Load(builder);
        }

        const int Horizontal = 0x0001;
        const int Minimum = 0x0002;
        const int CrossStream = 0x0004;
        const int Override = 0x0008;

        // The values of several subtables add up; a subtable can replace what the previous ones gave.
        var font = Load(Subtable(Horizontal, (2, 3, -100), (2, 4, 10)), Subtable(Horizontal, (2, 3, 30), (5, 6, 7)));
        Assert.Equal(-70, font.Face.GetLegacyKerning(2, 3));
        Assert.Equal(10, font.Face.GetLegacyKerning(2, 4));
        Assert.Equal(7, font.Face.GetLegacyKerning(5, 6));
        font = Load(Subtable(Horizontal, (2, 3, -100), (2, 4, 10)), Subtable(Horizontal | Override, (2, 3, 30)));
        Assert.Equal(30, font.Face.GetLegacyKerning(2, 3));
        Assert.Equal(10, font.Face.GetLegacyKerning(2, 4));

        // Vertical kerning, minimum values, cross-stream values and the class-based format 2 are not applied.
        font = Load(Subtable(0, (2, 3, 1)), Subtable(Horizontal | Minimum, (2, 3, 2)), Subtable(Horizontal | CrossStream, (2, 3, 4)), Subtable(Horizontal | 0x0200, (2, 3, 8)), Subtable(Horizontal, (2, 3, 16)));
        Assert.Equal(16, font.Face.GetLegacyKerning(2, 3));
        Assert.False(Load(Subtable(Horizontal | Minimum, (2, 3, 2))).Face.HasLegacyKerning);

        // The version 1.0 of the table, which Apple defines: a 32-bit header, then subtables with a 32-bit length, a
        // coverage (bit 15: vertical, the format in the low byte) and a tuple index. The pairs of format 0 are the ones
        // of version 0; a vertical subtable and a subtable of another format are skipped.
        static byte[] AppleSubtable(int coverage, params (int Left, int Right, int Value)[] pairs)
        {
            var writer = new FontWriter();
            writer.UInt32((uint)(16 + (pairs.Length * 6)));
            writer.UInt16(coverage);
            writer.UInt16(0);
            writer.UInt16(pairs.Length);
            writer.Zeros(6);
            foreach (var (left, right, value) in pairs)
            {
                writer.UInt16(left);
                writer.UInt16(right);
                writer.Int16(value);
            }

            return writer.ToArray();
        }

        var builder = FontSamples.Boxes();
        builder.RawTables["kern"] = [0, 1, 0, 0, 0, 0, 0, 3, .. AppleSubtable(0x8000, (2, 3, 1)), .. AppleSubtable(0x0002, (2, 3, 2)), .. AppleSubtable(0, (2, 3, 5))];
        Assert.Equal(5, FontTestSupport.Load(builder).Face.GetLegacyKerning(2, 3));
        builder.RawTables["kern"] = [0, 1, 0, 0, 0, 0, 0, 1, .. Subtable(Horizontal, (2, 3, 5))];
        Assert.False(FontTestSupport.Load(builder).Face.HasLegacyKerning);

        // A table too short for its header is not read.
        builder.RawTables["kern"] = [0, 0];
        Assert.False(FontTestSupport.Load(builder).Face.HasLegacyKerning);

        // A subtable that declares more pairs than the table holds is not used; the ones before it are.
        var truncated = Subtable(Horizontal, (2, 3, 5), (2, 4, 6));
        font = Load(Subtable(Horizontal, (2, 3, 1)), truncated[..^3]);
        Assert.Equal(1, font.Face.GetLegacyKerning(2, 3));
        Assert.Equal(0, font.Face.GetLegacyKerning(2, 4));

        // A single subtable whose 16-bit length cannot hold its pairs: the number of pairs is what counts. The font has
        // 113 glyphs, and the pairs are the first 11,000 of their combinations.
        var pairs = Enumerable.Range(0, 11000).Select(index => (Left: index / 113, Right: index % 113, Value: (index % 200) - 100)).ToArray();
        Assert.True(14 + (pairs.Length * 6) > ushort.MaxValue);
        builder = FontSamples.Boxes();
        for (var i = 0; i < 100; i++)
        {
            builder.AddGlyph(FontGlyph.Empty(500));
        }

        builder.RawTables["kern"] = [0, 0, 0, 1, .. Subtable(Horizontal, pairs)];
        font = FontTestSupport.Load(builder);
        Assert.Equal(-100, font.Face.GetLegacyKerning(0, 0));
        Assert.Equal(-88, font.Face.GetLegacyKerning(0, 12));
        Assert.Equal((10999 % 200) - 100, font.Face.GetLegacyKerning(97, 38));
        Assert.Equal(0, font.Face.GetLegacyKerning(97, 39));
    }

    [Fact]
    public void TheCaretsOfALigatureAreReadFromTheGlyphDefinitionTable()
    {
        // A GDEF header (version 1.0) whose list of ligature carets covers two glyphs: the box A with two carets of
        // format 1 (a coordinate), the box B with a caret of format 2 (a point of the outline), which is not read.
        ushort[] gdef =
        [
            1, 0, 0, 0, 12, 0,

            // The list: the coverage, the number of ligatures and their offsets.
            30, 2, 8, 22,

            // The box A: two carets, then the carets.
            2, 6, 10, 1, 100, 1, 300,

            // The box B.
            1, 4, 2, 5,

            // The coverage, format 1.
            1, 2, FontSamples.BoxA, FontSamples.BoxB,
        ];
        var builder = FontSamples.Boxes();
        builder.RawTables["GDEF"] = [.. gdef.SelectMany(value => new[] { (byte)(value >> 8), (byte)value })];
        var face = FontTestSupport.Load(builder).Face;

        Span<int> carets = stackalloc int[4];
        Assert.Equal(2, face.GetLigatureCarets(FontSamples.BoxA, carets));
        Assert.Equal([100, 300], carets[..2].ToArray());
        Assert.Equal(0, face.GetLigatureCarets(FontSamples.BoxB, carets));
        Assert.Equal(0, face.GetLigatureCarets(FontSamples.BoxC, carets));
        Assert.Equal(0, face.GetLigatureCarets(10_000, carets));

        // The carets beyond the room given are left out.
        Assert.Equal(1, face.GetLigatureCarets(FontSamples.BoxA, carets[..1]));
        Assert.Equal(100, carets[0]);

        // A font without the table has no carets.
        Assert.Equal(0, FontTestSupport.Load(FontSamples.Boxes()).Face.GetLigatureCarets(FontSamples.BoxA, carets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheAdvancesAreReadFromTheHorizontalMetrics(bool compact)
    {
        var builder = FontSamples.Boxes();
        builder.CompactHorizontalMetrics = compact;

        // Three trailing glyphs with the same advance: a compact table stores it once.
        builder.AddGlyph(FontGlyph.Rectangle(600, 0, 0, 100, 100));
        builder.AddGlyph(FontGlyph.Rectangle(600, 0, 0, 100, 100));
        builder.AddGlyph(FontGlyph.Rectangle(600, 0, 0, 100, 100));
        var font = FontTestSupport.Load(builder);

        Assert.Equal(512, font.Face.GetAdvanceWidth(FontSamples.NotdefGlyph));
        Assert.Equal(1024, font.Face.GetAdvanceWidth(FontSamples.BoxA));
        Assert.Equal(0, font.Face.GetAdvanceWidth(FontSamples.AcuteMark));
        Assert.Equal(600, font.Face.GetAdvanceWidth(13));
        Assert.Equal(600, font.Face.GetAdvanceWidth(14));
        Assert.Equal(600, font.Face.GetAdvanceWidth(15));
        Assert.Equal(0, font.Face.GetAdvanceWidth(16));
        Assert.Equal(0, font.Face.GetAdvanceWidth(-1));
    }

    [Theory]
    [InlineData((int)FontGlyfEncoding.Compact, false)]
    [InlineData((int)FontGlyfEncoding.Compact, true)]
    [InlineData((int)FontGlyfEncoding.Long, false)]
    [InlineData((int)FontGlyfEncoding.Long, true)]
    public void TheOutlinesOfSimpleGlyphsAreDecodedFromEveryEncoding(int encoding, bool longLoca)
    {
        var builder = FontSamples.Boxes();
        builder.GlyfEncoding = (FontGlyfEncoding)encoding;
        builder.UseLongLoca = longLoca;
        var font = FontTestSupport.Load(builder);

        // The geometry Common/FontSamples.cs describes, each contour closed by a line to its first point.
        Assert.Equal("M64,0L64,768L448,768L448,0L64,0ZM128,64L384,64L384,704L128,704L128,64Z", FontTestSupport.GetOutline(font, FontSamples.NotdefGlyph));
        Assert.Equal("", FontTestSupport.GetOutline(font, FontSamples.SpaceGlyph));
        Assert.Equal("M128,0L128,768L896,768L896,0L128,0Z", FontTestSupport.GetOutline(font, FontSamples.BoxA));
        Assert.Equal("M0,-192L0,832L768,832L768,-192L0,-192Z", FontTestSupport.GetOutline(font, FontSamples.BoxC));
        Assert.Equal("M0,0L256,768L512,0L0,0Z", FontTestSupport.GetOutline(font, FontSamples.TriangleD));
        Assert.Equal("M0,0L0,512L512,512L512,0L0,0ZM256,256L256,768L768,768L768,256L256,256Z", FontTestSupport.GetOutline(font, FontSamples.OverlapF));
        Assert.Equal("M-640,832L-640,960L-384,960L-384,832L-640,832Z", FontTestSupport.GetOutline(font, FontSamples.AcuteMark));

        // A glyph the font does not have has no outline.
        Assert.Equal("", FontTestSupport.GetOutline(font, 500));
        Assert.Equal("", FontTestSupport.GetOutline(font, -1));
    }

    [Fact]
    public void QuadraticContoursAreCompletedWithTheirImpliedPoints()
    {
        var font = FontTestSupport.Load(FontSamples.Curves());

        // No on-curve point: the contour starts half way between its last and its first control points, and each pair
        // of control points has an on-curve point half way between them.
        Assert.Equal("M768,256Q512,0 256,256Q0,512 256,768Q512,1024 768,768Q1024,512 768,256Z", FontTestSupport.GetOutline(font, 1));

        // The first point is a control point and the last one is on the curve: the contour starts at the last point.
        Assert.Equal("M256,0Q0,512 256,768L768,768Q1024,512 768,0L256,0Z", FontTestSupport.GetOutline(font, 2));

        // Two consecutive control points between two on-curve points.
        Assert.Equal("M0,0Q0,512 256,768Q512,1024 1024,1024L1024,0L0,0Z", FontTestSupport.GetOutline(font, 3));

        // A square and a round hole.
        Assert.Equal("M0,0L0,1024L1024,1024L1024,0L0,0ZM384,384Q512,256 640,384Q768,512 640,640Q512,768 384,640Q256,512 384,384Z", FontTestSupport.GetOutline(font, 4));
    }

    [Fact]
    public void TheOutlinesOfCffGlyphsAreDecodedFromTheirCharstrings()
    {
        var font = FontTestSupport.Load(FontSamples.Charstrings());

        // Computed by hand from the charstrings of Common/FontSamples.cs: every operator moves from the current point.
        Assert.Equal(11, font.GlyphCount);
        Assert.Equal("M100,0L100,700L400,700L400,0L100,0Z", FontTestSupport.GetOutline(font, 0));
        Assert.Equal("M50,0L150,0L150,200L50,200L50,0ZM50,300L50,400L150,400L150,300L50,300Z", FontTestSupport.GetOutline(font, 1));
        Assert.Equal("M0,0C0,100 100,200 200,200C300,200 400,100 400,0C400,-50 350,-100 300,-100L200,-100L100,-100C50,-100 0,-50 0,0Z", FontTestSupport.GetOutline(font, 2));
        Assert.Equal("M100,100C110,150 130,180 130,220C130,230 150,260 150,300C150,290 130,260 130,220C140,225 160,255 200,255C190,255 170,225 130,225L100,100Z", FontTestSupport.GetOutline(font, 3));
        Assert.Equal("M0,0C10,0 30,30 30,70C40,70 60,100 60,140C60,190 120,260 200,260C210,260 230,290 235,330C235,340 255,370 295,370C295,380 315,410 355,410C405,410 465,480 472,560L0,0Z", FontTestSupport.GetOutline(font, 4));
        Assert.Equal("M0,0C10,0 30,30 70,30C120,30 180,0 250,0C251,2 254,6 259,12C266,20 275,30 286,42C287,44 290,48 295,48C301,48 308,56 317,42C327,43 337,44 347,45C357,46 367,47 387,42C388,52 389,62 390,72C391,82 392,92 387,112L0,0Z", FontTestSupport.GetOutline(font, 5));
        Assert.Equal("M10,10L110,10L110,110L10,110L10,10Z", FontTestSupport.GetOutline(font, 6));
        Assert.Equal("M20,20L120,20L120,120L20,120L20,20Z", FontTestSupport.GetOutline(font, 7));
        Assert.Equal("M108,-108L1239,-1239L2371,-2371L2373,-2471L108,-108Z", FontTestSupport.GetOutline(font, 8));
        Assert.Equal("M15,15L25,15L25,25L15,15ZM115,15L125,25L115,15Z", FontTestSupport.GetOutline(font, 9));
        Assert.Equal("", FontTestSupport.GetOutline(font, 10));
        Assert.Equal("", FontTestSupport.GetOutline(font, 11));

        // The advances come from the horizontal metrics, never from the widths of the charstrings.
        Assert.Equal(500, font.Face.GetAdvanceWidth(0));
        Assert.Equal(400, font.Face.GetAdvanceWidth(6));
        Assert.Equal(FontOutlineKind.Cff, font.Face.Outlines);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ACidKeyedFontDecodesEachGlyphWithItsFontDict(int selectorFormat)
    {
        var font = FontTestSupport.Load(FontSamples.CidKeyed(selectorFormat));

        // The subroutine 0 is a horizontal line for the glyphs 0 and 3, and a vertical line for the glyphs 1 and 2.
        Assert.Equal("M0,0L100,0L100,50L0,0Z", FontTestSupport.GetOutline(font, 0));
        Assert.Equal("M0,0L0,100L50,100L0,0Z", FontTestSupport.GetOutline(font, 1));
        Assert.Equal("M10,10L10,110L60,110L10,10Z", FontTestSupport.GetOutline(font, 2));
        Assert.Equal("M10,10L110,10L110,60L10,10Z", FontTestSupport.GetOutline(font, 3));
    }

    [Fact]
    public void TheArithmeticOperatorsOfACharstringAreEvaluated()
    {
        // Each expression leaves one value on the stack, which becomes the length of a horizontal line.
        (double Expected, Action<FontCharstring> Expression)[] cases =
        [
            (1, cs => cs.Escape(FontCharstring.And, 5, 3)),
            (0, cs => cs.Escape(FontCharstring.And, 5, 0)),
            (0, cs => cs.Escape(FontCharstring.Or, 0, 0)),
            (1, cs => cs.Escape(FontCharstring.Or, 0, 7)),
            (1, cs => cs.Escape(FontCharstring.Not, 0)),
            (0, cs => cs.Escape(FontCharstring.Not, 5)),
            (7, cs => cs.Escape(FontCharstring.Abs, -7)),
            (7, cs => cs.Escape(FontCharstring.Add, 3, 4)),
            (6, cs => cs.Escape(FontCharstring.Sub, 10, 4)),
            (4.5, cs => cs.Escape(FontCharstring.Div, 9, 2)),
            (-5, cs => cs.Escape(FontCharstring.Neg, 5)),
            (1, cs => cs.Escape(FontCharstring.Eq, 4, 4)),
            (0, cs => cs.Escape(FontCharstring.Eq, 4, 5)),
            (8, cs => cs.Escape(FontCharstring.Drop, 8, 9)),
            (42, cs => cs.Escape(FontCharstring.Put, 42, 3).Escape(FontCharstring.Get, 3)),
            (0, cs => cs.Escape(FontCharstring.Get, 31)),
            (10, cs => cs.Escape(FontCharstring.IfElse, 10, 20, 1, 2)),
            (10, cs => cs.Escape(FontCharstring.IfElse, 10, 20, 2, 2)),
            (20, cs => cs.Escape(FontCharstring.IfElse, 10, 20, 3, 2)),
            (1, cs => cs.Escape(FontCharstring.Random)),
            (42, cs => cs.Escape(FontCharstring.Mul, 6, 7)),
            (7, cs => cs.Escape(FontCharstring.Sqrt, 49)),
            (10, cs => cs.Escape(FontCharstring.Dup, 5).Escape(FontCharstring.Add)),
            (-6, cs => cs.Escape(FontCharstring.Exch, 10, 4).Escape(FontCharstring.Sub)),

            // 7 8 9 1 index gives 7 8 9 8: the value under the top. The three other values are then dropped.
            (8, cs => cs.Escape(FontCharstring.Index, 7, 8, 9, 1).Escape(FontCharstring.Exch).Escape(FontCharstring.Drop).Escape(FontCharstring.Exch).Escape(FontCharstring.Drop).Escape(FontCharstring.Exch).Escape(FontCharstring.Drop)),

            // A negative index is the top: 7 9 9, then 7 + (9 - 9).
            (7, cs => cs.Escape(FontCharstring.Index, 7, 9, -1).Escape(FontCharstring.Sub).Escape(FontCharstring.Add)),

            // 1 2 3 4, the top three rolled by one toward the top: 1 4 2 3, then 1 + 4 * (2 - 3).
            (-3, cs => cs.Escape(FontCharstring.Roll, 1, 2, 3, 4, 3, 1).Escape(FontCharstring.Sub).Escape(FontCharstring.Mul).Escape(FontCharstring.Add)),

            // Rolled the other way: 1 3 4 2, then 1 + 3 * (4 - 2). Four positions is no change: 1 + 2 * (3 - 4).
            (7, cs => cs.Escape(FontCharstring.Roll, 1, 2, 3, 4, 3, -1).Escape(FontCharstring.Sub).Escape(FontCharstring.Mul).Escape(FontCharstring.Add)),
            (-1, cs => cs.Escape(FontCharstring.Roll, 1, 2, 3, 4, 4, 4).Escape(FontCharstring.Sub).Escape(FontCharstring.Mul).Escape(FontCharstring.Add)),

            // Numbers with a fraction: HarfBuzz gives the same outline; FreeType rounds it.
            (0.5, cs => cs.Fixed(0.5)),
            (-100.25, cs => cs.Fixed(-100.25)),
        ];

        var cff = new FontCffWriter();
        foreach (var (_, expression) in cases)
        {
            var charstring = new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0);
            expression(charstring);
            cff.CharStrings.Add(charstring.Op(FontCharstring.RLineTo, 0).Op(FontCharstring.RLineTo, 0, 10).Op(FontCharstring.EndChar).ToArray());
        }

        var font = LoadCff(cff);
        for (var i = 0; i < cases.Length; i++)
        {
            var x = cases[i].Expected.ToString("0.##", CultureInfo.InvariantCulture);
            Assert.Equal($"M0,0L{x},0L{x},10L0,0Z", FontTestSupport.GetOutline(font, i));
        }
    }

    [Fact]
    public void ACharstringEndsWhereItsBytesEnd()
    {
        // No endchar: the contour is closed all the same. A subroutine that ends without return goes back to its caller.
        var cff = new FontCffWriter();
        cff.LocalSubroutines[0].Add(new FontCharstring().Op(FontCharstring.RLineTo, 0, 10).ToArray());
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 10, 0).Call(FontCharstring.CallSubr, 0, 1).Op(FontCharstring.RLineTo, -5, 0).ToArray());

        // A drawing operator without moveto starts at the origin, and the deprecated dotsection operator does nothing.
        cff.CharStrings.Add(new FontCharstring().Escape(0).Op(FontCharstring.RLineTo, 10, 0, 0, 10).Op(FontCharstring.EndChar).ToArray());

        // An empty charstring.
        cff.CharStrings.Add([]);
        var font = LoadCff(cff);

        Assert.Equal("M0,0L10,0L10,10L5,10L0,0Z", FontTestSupport.GetOutline(font, 0));
        Assert.Equal("M0,0L10,0L10,10L0,0Z", FontTestSupport.GetOutline(font, 1));
        Assert.Equal("", FontTestSupport.GetOutline(font, 2));
    }

    [Fact]
    public void AMalformedCharstringIsRejectedWhenItsGlyphIsDecoded()
    {
        (string Message, Action<FontCharstring> Charstring)[] cases =
        [
            ("too few arguments", cs => cs.Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 1)),
            ("too few arguments", cs => cs.Op(FontCharstring.RMoveTo, 0)),
            ("too few arguments", cs => cs.Op(FontCharstring.RRCurveTo, 1, 2, 3, 4, 5)),
            ("too few arguments", cs => cs.Op(FontCharstring.CallSubr)),
            ("too few arguments", cs => cs.Escape(FontCharstring.Add, 1)),
            ("too few arguments", cs => cs.Escape(FontCharstring.Flex, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12)),
            ("subroutine that does not exist", cs => cs.Call(FontCharstring.CallSubr, 3, 2)),
            ("subroutine that does not exist", cs => cs.Call(FontCharstring.CallGSubr, 0, 0)),
            ("subroutine that does not exist", cs => cs.Fixed(-106.5).Op(FontCharstring.CallSubr)),
            ("nests more subroutine calls", cs => cs.Call(FontCharstring.CallSubr, 0, 2)),
            ("reserved operator 2", cs => cs.Op(2)),
            ("reserved operator 9", cs => cs.Op(9)),
            ("reserved operator 16", cs => cs.Op(16, 1)),
            ("reserved operator 12 1", cs => cs.Escape(1)),
            ("reserved operator 12 38", cs => cs.Escape(38)),
            ("divides by zero", cs => cs.Escape(FontCharstring.Div, 1, 0)),
            ("square root of a negative", cs => cs.Escape(FontCharstring.Sqrt, -1)),
            ("outside the transient array", cs => cs.Escape(FontCharstring.Put, 1, 32)),
            ("outside the transient array", cs => cs.Escape(FontCharstring.Get, -1)),
            ("below the stack", cs => cs.Escape(FontCharstring.Index, 1, 2, 2)),
            ("rolls more arguments", cs => cs.Escape(FontCharstring.Roll, 1, 2, 3, 1)),
            ("more arguments than the stack", cs => cs.Numbers([.. Enumerable.Repeat(1, 514)])),
            ("more arguments than the stack", cs => cs.Numbers([.. Enumerable.Repeat(1, 513)]).Escape(FontCharstring.Dup)),
            ("ends inside a hint mask", cs => cs.Op(FontCharstring.HStemHM, 1, 2).Op(FontCharstring.HintMask)),
            ("truncated", cs => cs.Raw(28, 1)),
            ("truncated", cs => cs.Raw(255, 1, 2, 3)),
            ("truncated", cs => cs.Raw(247)),
            ("truncated", cs => cs.Raw(12)),
            ("outside the coordinates", cs => cs.Op(FontCharstring.RMoveTo, 0, 0).Numbers(30000, 30000).Escape(FontCharstring.Mul).Op(FontCharstring.RLineTo, 0)),
            ("outside the coordinates", cs => cs.Op(FontCharstring.RMoveTo, 0, 0).Numbers(30000).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Mul).Escape(FontCharstring.Dup).Escape(FontCharstring.Sub).Op(FontCharstring.RLineTo, 0)),
        ];

        var cff = new FontCffWriter();

        // Two local subroutines: one that calls itself, one that does nothing.
        cff.LocalSubroutines[0].Add(new FontCharstring().Call(FontCharstring.CallSubr, 0, 2).Op(FontCharstring.Return).ToArray());
        cff.LocalSubroutines[0].Add(new FontCharstring().Op(FontCharstring.Return).ToArray());
        foreach (var (_, build) in cases)
        {
            var charstring = new FontCharstring();
            build(charstring);
            cff.CharStrings.Add(charstring.ToArray());
        }

        // The font loads: a charstring is only read when its glyph is needed.
        var font = LoadCff(cff);
        for (var i = 0; i < cases.Length; i++)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(font, i));
            Assert.Contains(cases[i].Message, exception.Message);
            Assert.Equal(ImageFormat.Unknown, exception.Format);
        }
    }

    [Fact]
    public void TheFeaturesOfCffThatAreNotSupportedAreReported()
    {
        // An accented glyph made of two other glyphs by endchar, a form of Type 1 that the format deprecates.
        var cff = new FontCffWriter();
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.EndChar, 0, 0, 65, 96).ToArray());
        var font = LoadCff(cff);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => FontTestSupport.GetOutline(font, 0));
        Assert.Equal("Font: CFF seac", exception.Feature);
        Assert.Equal(ImageFormat.Unknown, exception.Format);

        // Type 1 charstrings in a CFF table.
        cff = new FontCffWriter { CharstringType = 1 };
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.EndChar).ToArray());
        exception = Assert.Throws<UnsupportedImageFeatureException>(() => LoadCff(cff));
        Assert.Equal("Font: Type 1 charstrings", exception.Feature);

        // A CFF table is not a CFF2 table, whatever its tag says.
        var builder = FontSamples.Charstrings();
        builder.RawTables["CFF2"] = builder.RawTables["CFF "];
        builder.RawTables.Remove("CFF ");
        var invalid = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.Load(builder));
        Assert.Contains("'CFF2' table has the unknown version 1", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAccentedGlyphsOfCffAreGivenToTheCallersThatAcceptThem()
    {
        // The PDF renderer draws the base character and the accent of the deprecated form of endchar itself
        var cff = new FontCffWriter();
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 10, 0, 0, 10).Op(FontCharstring.EndChar, 120, 180, 65, 194).ToArray());
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 10, 0).Op(FontCharstring.EndChar).ToArray());
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4, UnitsPerEm = 1000 };
        builder.AddGlyph(FontGlyph.Empty(500));
        builder.AddGlyph(FontGlyph.Empty(500));
        cff.Apply(builder);
        var data = builder.RawTables["CFF "];
        var table = CffTable.Read(data);

        var sink = new FontTestSupport.SvgPathSink();
        var budget = new GlyphBudget(FontResourceLimits.Default);
        var accented = table.DecodeWithAccentedGlyphs(data, 0, sink, ref budget);
        Assert.Equal(new CffAccentedGlyph(120, 180, 65, 194), accented);
        Assert.Equal("M0,0L10,0L10,10L0,0Z", sink.ToString());

        sink = new FontTestSupport.SvgPathSink();
        Assert.Null(table.DecodeWithAccentedGlyphs(data, 1, sink, ref budget));
        Assert.Equal("M0,0L10,0L0,0Z", sink.ToString());
    }

    [Fact]
    public void TheWorkOfACharstringIsLimited()
    {
        // One operation per operator and per segment: rmoveto, rlineto and its two lines, endchar.
        var cff = new FontCffWriter();
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Op(FontCharstring.RLineTo, 10, 0, 0, 10).Op(FontCharstring.EndChar).ToArray());

        // Nine subroutines that each call the next one five times, the last one drawing lines: 5^9 lines, about two
        // million, from a font of a few hundred bytes.
        for (var i = 0; i < 9; i++)
        {
            var subroutine = new FontCharstring();
            for (var call = 0; call < 5; call++)
            {
                if (i == 8)
                {
                    subroutine.Op(FontCharstring.RLineTo, 1, 1);
                }
                else
                {
                    subroutine.Call(FontCharstring.CallSubr, i + 1, 9);
                }
            }

            cff.LocalSubroutines[0].Add(subroutine.Op(FontCharstring.Return).ToArray());
        }

        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 0, 0).Call(FontCharstring.CallSubr, 0, 9).Op(FontCharstring.EndChar).ToArray());

        static FontLoadOptions Options(long complexity) => new() { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxGlyphComplexity = complexity } } };

        Assert.Equal("M0,0L10,0L10,10L0,0Z", FontTestSupport.GetOutline(LoadCff(cff, Options(5)), 0));
        var exception = Assert.Throws<ImageResourceLimitException>(() => FontTestSupport.GetOutline(LoadCff(cff, Options(4)), 0));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, exception.Kind);
        Assert.Equal(4, exception.Limit);
        Assert.Equal(5, exception.Requested);

        var bomb = LoadCff(cff);
        Assert.HasCountLessThan(1024, bomb.Face.Data.RawBytes);
        exception = Assert.Throws<ImageResourceLimitException>(() => FontTestSupport.GetOutline(bomb, 1));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, exception.Kind);
        Assert.Equal(ImageResourceLimits.DefaultMaxGlyphComplexity, exception.Limit);
    }

    [Fact]
    public void AMalformedCffTableIsRejectedWhenTheFontIsLoaded()
    {
        // The table of the sample: a header of 4 bytes, then the INDEX of the names (a count of 1, offsets of 1 byte).
        var valid = FontSamples.Charstrings().RawTables["CFF "];
        Assert.Equal(new byte[] { 1, 0, 4, 4, 0, 1, 1, 1 }, valid[..8]);

        static void AssertInvalid(byte[] table, string expectedMessage)
        {
            var builder = FontSamples.Charstrings();
            builder.RawTables["CFF "] = table;
            var exception = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.Load(builder));
            Assert.Contains(expectedMessage, exception.Message);
            Assert.Equal(ImageFormat.Unknown, exception.Format);
        }

        byte[] Edit(Action<byte[]> edit)
        {
            var copy = (byte[])valid.Clone();
            edit(copy);
            return copy;
        }

        AssertInvalid(Edit(data => data[0] = 2), "unknown version 2");
        AssertInvalid(Edit(data => data[2] = 3), "header shorter");
        AssertInvalid(Edit(data => data[2] = 200), "invalid");
        AssertInvalid(Edit(data => data[6] = 0), "offsets of 0 bytes");
        AssertInvalid(Edit(data => data[6] = 5), "offsets of 5 bytes");
        AssertInvalid([1, 0, 4, 4, 0, 1, 1, 1, 250], "outside the table");
        AssertInvalid(Edit(data => data[5] = 200), "invalid");
        AssertInvalid([1, 0, 4, 4], "INDEX outside");

        // A table without font, with two fonts, and with a Top DICT that does not place the charstrings.
        byte[] empty = [0, 0];
        AssertInvalid([1, 0, 4, 4, .. empty, .. empty, .. empty, .. empty], "holds 0 fonts");
        AssertInvalid([1, 0, 4, 4, .. empty, 0, 2, 1, 1, 1, 1, .. empty, .. empty], "holds 2 fonts");
        AssertInvalid([1, 0, 4, 4, .. empty, 0, 1, 1, 1, 1, .. empty, .. empty], "does not say where its charstrings are");

        // Top DICTs: an offset that is negative, a real number where an offset is expected, operands without operator,
        // a reserved byte, a real number without end, a charstring INDEX that is empty.
        static byte[] WithTopDict(params byte[] dict) => [1, 0, 4, 4, 0, 0, 0, 1, 1, 1, (byte)(dict.Length + 1), .. dict, 0, 0, 0, 0];
        AssertInvalid(WithTopDict(138, 17), "not a position of the table");
        AssertInvalid(WithTopDict(30, 0x1A, 0x5F, 17), "not a position of the table");
        AssertInvalid(WithTopDict(139, 139), "operands without operator");
        AssertInvalid(WithTopDict(22, 17), "reserved byte 22");
        AssertInvalid(WithTopDict(30, 0x12, 0x34), "truncated");
        AssertInvalid(WithTopDict(30, 0xDD, 0xFF, 17), "malformed real number");
        AssertInvalid(WithTopDict([.. Enumerable.Repeat((byte)139, 49), 17]), "too many operands");
        AssertInvalid(WithTopDict(139 + 13, 17), "no charstring");

        // A CID-keyed font without its font DICTs, and one whose selector has an unknown format.
        AssertInvalid(WithTopDict(139, 139, 139, 12, 30, 139 + 13, 17), "INDEX");
        var cid = FontSamples.CidKeyed(0);
        var table = cid.RawTables["CFF "];
        Assert.Equal(new byte[] { 0, 0, 1, 1, 0 }, table[^5..]);
        table[^5] = 7;
        var cidException = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.Load(cid));
        Assert.Contains("selector of the unknown format 7", cidException.Message);

        // A glyph whose font DICT does not exist is found when the glyph is decoded.
        foreach (var format in (int[])[0, 3])
        {
            var builder = FontSamples.CidKeyed(format);
            table = builder.RawTables["CFF "];
            if (format == 0)
            {
                table[^3] = 9;
            }
            else
            {
                // The ranges end with the font DICT of the last one and the number of glyphs.
                Assert.Equal(new byte[] { 0, 0, 4 }, table[^3..]);
                table[^3] = 9;
            }

            var font = FontTestSupport.Load(builder);
            Assert.Equal("M0,0L100,0L100,50L0,0Z", FontTestSupport.GetOutline(font, 0));
            var glyph = format == 0 ? 1 : 3;
            cidException = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(font, glyph));
            Assert.Contains("font DICT that does not exist", cidException.Message);
        }
    }

    [Fact]
    public void EveryTruncationOfACffFontIsRejectedOrGivesAUsableFont()
    {
        foreach (var data in (byte[][])[FontSamples.Charstrings().Build(), FontSamples.CidKeyed().Build()])
        {
            for (var length = 0; length < data.Length; length++)
            {
                try
                {
                    var font = Font.Load(data.AsSpan(0, length));
                    for (var glyph = 0; glyph < font.GlyphCount; glyph++)
                    {
                        try
                        {
                            FontTestSupport.GetOutline(font, glyph);
                        }
                        catch (InvalidImageContentException)
                        {
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidImageContentException or UnknownImageFormatException)
                {
                }
            }
        }
    }

    /// <summary>Loads a font whose glyphs are the charstrings of a CFF table.</summary>
    private static Font LoadCff(FontCffWriter cff, FontLoadOptions? options = null)
    {
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4, UnitsPerEm = 1000 };
        for (var i = 0; i < cff.CharStrings.Count; i++)
        {
            builder.AddGlyph(FontGlyph.Empty(500));
        }

        cff.Apply(builder);
        return FontTestSupport.Load(builder, options);
    }

    [Fact]
    public void DegenerateContoursAreKeptAsTheyAre()
    {
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        builder.AddGlyph(FontGlyph.Empty(500));
        builder.AddGlyph(FontGlyph.Polygons(500, [(0, 0), (0, 100), (100, 100), (0, 0)]), 'a');
        builder.AddGlyph(FontGlyph.Polygons(500, [(0, 0), (0, 0), (100, 100), (100, 100)]), 'b');
        var glyph = new FontGlyph { AdvanceWidth = 500 };
        glyph.Contours.Add([new FontPoint(0, 0), new FontPoint(50, 100, false), new FontPoint(0, 0)]);
        glyph.Contours.Add([new FontPoint(7, 7, false)]);
        glyph.Contours.Add([new FontPoint(7, 7, false), new FontPoint(9, 9, false)]);
        glyph.Contours.Add([new FontPoint(1, 1), new FontPoint(2, 2)]);
        glyph.Contours.Add([new FontPoint(5, 6)]);
        builder.AddGlyph(glyph, 'c');
        var font = FontTestSupport.Load(builder);

        // What hb-vector 14.6.0 prints for this font, with its own reader and with FreeType: repeated points are kept,
        // and the line that closes a contour is always there.
        Assert.Equal("M0,0L0,100L100,100L0,0L0,0Z", FontTestSupport.GetOutline(font, 1));
        Assert.Equal("M0,0L0,0L100,100L100,100L0,0Z", FontTestSupport.GetOutline(font, 2));
        Assert.Equal("M0,0Q50,100 0,0L0,0ZM7,7Q7,7 7,7ZM8,8Q7,7 8,8Q9,9 8,8ZM1,1L2,2L1,1ZM5,6L5,6Z", FontTestSupport.GetOutline(font, 3));
    }

    [Fact]
    public void CompositeGlyphsPlaceAndTransformTheirComponents()
    {
        var font = FontTestSupport.Load(FontSamples.Boxes());

        // A, then the mark moved by (1152, 0).
        Assert.Equal("M128,0L128,768L896,768L896,0L128,0ZM512,832L512,960L768,960L768,832L512,832Z", FontTestSupport.GetOutline(font, FontSamples.CompositeAacute));

        // B, (64, 0)-(448, 512), scaled by 1.5 then moved by (64, 0).
        Assert.Equal("M160,0L160,768L736,768L736,0L160,0Z", FontTestSupport.GetOutline(font, FontSamples.ScaledH));
    }

    [Fact]
    public void ACompositeGlyphAppliesEveryFormOfTransformation()
    {
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        builder.AddGlyph(FontGlyph.Empty(500));
        var square = builder.AddGlyph(FontGlyph.Rectangle(500, 0, 0, 100, 200), 'a');

        // Different scales on the two axes.
        var stretched = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = square, XScale = 0.5, YScale = 1.5, Argument1 = 10, Argument2 = 20 }));

        // A quarter turn: x' = -y, y' = x.
        var turned = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = square, XScale = 0, YScale = 0, Scale01 = 1, Scale10 = -1 }));

        // The offset is transformed with the component when the font asks for it, and never otherwise.
        var scaledOffset = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = square, XScale = 0.5, YScale = 0.5, Argument1 = 100, Argument2 = 40, ScaledComponentOffset = true }));
        var unscaledOffset = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = square, XScale = 0.5, YScale = 0.5, Argument1 = 100, Argument2 = 40, UnscaledComponentOffset = true }));

        // Offsets that need 16 bits, and a composite of composites.
        var far = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = square, Argument1 = 1000, Argument2 = -300 }));
        var nested = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = stretched, Argument1 = 1, Argument2 = 2 }, new FontComponent { GlyphId = far, XScale = 0.5, YScale = 0.5 }));
        var font = FontTestSupport.Load(builder);

        Assert.Equal("M10,20L10,320L60,320L60,20L10,20Z", FontTestSupport.GetOutline(font, stretched));
        Assert.Equal("M0,0L-200,0L-200,100L0,100L0,0Z", FontTestSupport.GetOutline(font, turned));
        Assert.Equal("M50,20L50,120L100,120L100,20L50,20Z", FontTestSupport.GetOutline(font, scaledOffset));
        Assert.Equal("M100,40L100,140L150,140L150,40L100,40Z", FontTestSupport.GetOutline(font, unscaledOffset));
        Assert.Equal("M1000,-300L1000,-100L1100,-100L1100,-300L1000,-300Z", FontTestSupport.GetOutline(font, far));
        Assert.Equal("M11,22L11,322L61,322L61,22L11,22ZM500,-150L500,-50L550,-50L550,-150L500,-150Z", FontTestSupport.GetOutline(font, nested));
    }

    [Fact]
    public void ACompositeGlyphCanPlaceAComponentByMatchingPoints()
    {
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        builder.AddGlyph(FontGlyph.Empty(500));
        var square = builder.AddGlyph(FontGlyph.Rectangle(500, 0, 0, 100, 100), 'a');
        var small = builder.AddGlyph(FontGlyph.Rectangle(500, 10, 10, 30, 30), 'b');

        // The point 0 of the small square, (10, 10), is moved onto the point 2 of the large one, (100, 100).
        var matched = FontGlyph.Composite(
            500,
            new FontComponent { GlyphId = square },
            new FontComponent { GlyphId = small, MatchesPoints = true, Argument1 = 2, Argument2 = 0 });
        matched.BoundsOverride = new FontBounds(0, 0, 120, 120);
        var glyph = builder.AddGlyph(matched);
        var font = FontTestSupport.Load(builder);

        Assert.Equal("M0,0L0,100L100,100L100,0L0,0ZM100,100L100,120L120,120L120,100L100,100Z", FontTestSupport.GetOutline(font, glyph));

        // A point that does not exist is an error of the font.
        var invalid = FontGlyph.Composite(
            500,
            new FontComponent { GlyphId = square },
            new FontComponent { GlyphId = small, MatchesPoints = true, Argument1 = 4, Argument2 = 0 });
        invalid.BoundsOverride = new FontBounds(0, 0, 120, 120);
        var invalidGlyph = builder.AddGlyph(invalid);
        var invalidFont = FontTestSupport.Load(builder);
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(invalidFont, invalidGlyph));
    }

    [Fact]
    public void ACompositeGlyphThatRefersToItselfIsRejected()
    {
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        builder.AddGlyph(FontGlyph.Empty(500));
        var loop = FontGlyph.Composite(500, new FontComponent { GlyphId = 1, Argument1 = 1 });
        loop.BoundsOverride = new FontBounds(0, 0, 10, 10);
        builder.AddGlyph(loop, 'a');
        var font = FontTestSupport.Load(builder);

        var exception = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(font, 1));
        Assert.Equal(ImageFormat.Unknown, exception.Format);

        // A component that is not a glyph of the font.
        var missing = FontGlyph.Composite(500, new FontComponent { GlyphId = 7 });
        missing.BoundsOverride = new FontBounds(0, 0, 10, 10);
        builder.Glyphs[1] = missing;
        var fontWithMissingComponent = FontTestSupport.Load(builder);
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(fontWithMissingComponent, 1));
    }

    [Fact]
    public void TheComplexityOfAGlyphIsLimited()
    {
        // A square is 4 points; a composite of two squares is 2 components and 8 points.
        var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        builder.AddGlyph(FontGlyph.Empty(500));
        var square = builder.AddGlyph(FontGlyph.Rectangle(500, 0, 0, 100, 100), 'a');
        var pair = builder.AddGlyph(FontGlyph.Composite(500, new FontComponent { GlyphId = square }, new FontComponent { GlyphId = square, Argument1 = 100 }));

        static Font Load(FontBuilder builder, long complexity)
            => FontTestSupport.Load(builder, new FontLoadOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxGlyphComplexity = complexity } } });

        Assert.Equal("M0,0L0,100L100,100L100,0L0,0Z", FontTestSupport.GetOutline(Load(builder, 4), square));
        var exception = Assert.Throws<ImageResourceLimitException>(() => FontTestSupport.GetOutline(Load(builder, 3), square));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, exception.Kind);
        Assert.Equal(3, exception.Limit);
        Assert.Equal(4, exception.Requested);

        Assert.NotEmpty(FontTestSupport.GetOutline(Load(builder, 10), pair));
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, Assert.Throws<ImageResourceLimitException>(() => FontTestSupport.GetOutline(Load(builder, 9), pair)).Kind);

        // Glyphs that double at each level: 2^20 squares from 21 small glyphs. The limit stops the expansion.
        var doubling = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
        doubling.AddGlyph(FontGlyph.Empty(500));
        var previous = doubling.AddGlyph(FontGlyph.Rectangle(500, 0, 0, 10, 10), 'a');
        for (var level = 0; level < 20; level++)
        {
            var composite = FontGlyph.Composite(500, new FontComponent { GlyphId = previous }, new FontComponent { GlyphId = previous, Argument1 = 1 });
            composite.BoundsOverride = new FontBounds(0, 0, 30, 10);
            previous = doubling.AddGlyph(composite);
        }

        var bomb = FontTestSupport.Load(doubling);
        Assert.HasCountLessThan(2048, doubling.Build());
        Assert.Equal(ImageResourceLimitKind.GlyphComplexity, Assert.Throws<ImageResourceLimitException>(() => FontTestSupport.GetOutline(bomb, previous)).Kind);
    }

    [Fact]
    public void TheBytesOfAFontAreLimited()
    {
        var data = FontSamples.Boxes().Build();
        static FontLoadOptions Options(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };

        // The bytes read, at the limit and one under.
        Assert.Equal(13, Font.Load(data, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length })).GlyphCount);
        var exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(data, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 })));
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, exception.Kind);
        Assert.Equal(data.Length - 1, exception.Limit);

        using (var stream = new MemoryStream(data))
        {
            Assert.Equal(13, Font.Load(stream, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length })).GlyphCount);
        }

        using (var stream = new MemoryStream(data))
        {
            Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Font.Load(stream, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        }

        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal(13, Font.Load(stream, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length })).GlyphCount);
        }

        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Font.Load(stream, Options(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        }

        // The bytes the font keeps.
        Assert.Equal(13, Font.Load(data, Options(new ImageResourceLimits { MaxFontBytes = data.Length })).GlyphCount);
        exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(data, Options(new ImageResourceLimits { MaxFontBytes = data.Length - 1 })));
        Assert.Equal(ImageResourceLimitKind.FontBytes, exception.Kind);
        Assert.Equal(data.Length - 1, exception.Limit);
        Assert.Equal(data.Length, exception.Requested);
        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal(ImageResourceLimitKind.FontBytes, Assert.Throws<ImageResourceLimitException>(() => Font.Load(stream, Options(new ImageResourceLimits { MaxFontBytes = data.Length - 1 }))).Kind);
        }
    }

    [Fact]
    public async Task AFontIsLoadedFromAPathAStreamOrASpan()
    {
        var data = FontSamples.Boxes().Build();
        var directory = Path.Combine(Path.GetTempPath(), "font-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "boxes.ttf");
            await File.WriteAllBytesAsync(path, data, TestContext.Current.CancellationToken);

            Assert.Equal("Meziantou Boxes", Font.Load(path).FamilyName);
            Assert.Equal("Meziantou Boxes", (await Font.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken)).FamilyName);
            Assert.Single(FontCollection.Load(path));
            Assert.Single(await FontCollection.LoadAsync(path, cancellationToken: TestContext.Current.CancellationToken));

            // A stream is read from its position and left open.
            using var stream = new MemoryStream([1, 2, 3, .. data]);
            stream.Position = 3;
            Assert.Equal("Meziantou Boxes", Font.Load(stream).FamilyName);
            stream.Position = 3;
            Assert.Equal("Meziantou Boxes", (await Font.LoadAsync(stream, cancellationToken: TestContext.Current.CancellationToken)).FamilyName);
            stream.Position = 3;
            Assert.Single(FontCollection.Load(stream));
            stream.Position = 3;
            Assert.Single(await FontCollection.LoadAsync(stream, cancellationToken: TestContext.Current.CancellationToken));
            Assert.True(stream.CanRead);

            using var forwardOnly = new NonSeekableStream(data);
            Assert.Equal("Meziantou Boxes", (await Font.LoadAsync(forwardOnly, cancellationToken: TestContext.Current.CancellationToken)).FamilyName);

            // The span is copied: changing it afterwards does not change the font.
            var copy = (byte[])data.Clone();
            var font = Font.Load(copy);
            Array.Clear(copy);
            Assert.Equal("Meziantou Boxes", font.FamilyName);
            Assert.Equal("M128,0L128,768L896,768L896,0L128,0Z", FontTestSupport.GetOutline(font, FontSamples.BoxA));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LoadingValidatesItsArguments()
    {
        Assert.Throws<ArgumentNullException>("path", () => Font.Load((string)null!));
        Assert.Throws<ArgumentException>("path", () => Font.Load(""));
        Assert.Throws<ArgumentNullException>("stream", () => Font.Load((Stream)null!));
        Assert.Throws<ArgumentNullException>("path", () => FontCollection.Load((string)null!));
        Assert.Throws<ArgumentException>("path", () => FontCollection.Load(""));
        Assert.Throws<ArgumentNullException>("stream", () => FontCollection.Load((Stream)null!));
        Assert.Throws<ArgumentNullException>("path", () => { _ = Font.LoadAsync((string)null!, cancellationToken: TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentNullException>("stream", () => { _ = Font.LoadAsync((Stream)null!, cancellationToken: TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentNullException>("path", () => { _ = FontCollection.LoadAsync((string)null!, cancellationToken: TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentNullException>("stream", () => { _ = FontCollection.LoadAsync((Stream)null!, cancellationToken: TestContext.Current.CancellationToken); });
        Assert.Throws<ArgumentNullException>(() => new FontLoadOptions { Configuration = null! });
        Assert.Same(ImageConfiguration.Default, FontLoadOptions.Default.Configuration);

        using var closed = new MemoryStream();
        closed.Dispose();
        Assert.Throws<ArgumentException>("stream", () => Font.Load(closed));
        Assert.Throws<ArgumentException>("stream", () => FontCollection.Load(closed));

        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        using var stream = new MemoryStream(FontSamples.Boxes().Build());
        await Assert.ThrowsAsync<OperationCanceledException>(() => Font.LoadAsync(stream, cancellationToken: canceled.Token));
        await Assert.ThrowsAsync<OperationCanceledException>(() => FontCollection.LoadAsync("missing.ttf", cancellationToken: canceled.Token));
        Assert.Throws<FileNotFoundException>(() => Font.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".ttf")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void AFontCollectionGivesEveryFace(int version)
    {
        var data = FontBuilder.WriteCollection([FontSamples.Boxes().BuildTables(), FontSamples.Curves().BuildTables()], version);

        var collection = FontCollection.Load(data);

        Assert.HasCount(2, collection);
        Assert.Equal("Meziantou Boxes", collection[0].FamilyName);
        Assert.Equal("Meziantou Curves", collection[1].FamilyName);
        Assert.Equal(new[] { "Meziantou Boxes", "Meziantou Curves" }, collection.Select(font => font.FamilyName).ToArray());
        Assert.Equal("M128,0L128,768L896,768L896,0L128,0Z", FontTestSupport.GetOutline(collection[0], FontSamples.BoxA));
        Assert.Equal("M768,256Q512,0 256,256Q0,512 256,768Q512,1024 768,768Q1024,512 768,256Z", FontTestSupport.GetOutline(collection[1], 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => collection[2]);
        Assert.Throws<ArgumentOutOfRangeException>(() => collection[-1]);

        // Loading a collection as a font gives its first face.
        Assert.Equal("Meziantou Boxes", Font.Load(data).FamilyName);
    }

    [Fact]
    public void DataThatIsNotAFontIsNotRecognized()
    {
        Assert.Throws<UnknownImageFormatException>(() => Font.Load([]));
        Assert.Throws<UnknownImageFormatException>(() => Font.Load([0, 1, 0]));
        Assert.Throws<UnknownImageFormatException>(() => Font.Load("not a font at all"u8));
        Assert.Throws<UnknownImageFormatException>(() => FontCollection.Load([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]));

        // A PostScript Type 1 font in an sfnt wrapper is recognized, and not supported.
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Font.Load("typ1\0\0\0\0\0\0\0\0"u8));
        Assert.Equal(ImageFormat.Unknown, exception.Format);
        Assert.StartsWith("Font: ", exception.Feature);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("hhea")]
    [InlineData("maxp")]
    [InlineData("hmtx")]
    [InlineData("cmap")]
    [InlineData("loca")]
    public void AFontWithoutARequiredTableIsInvalid(string tag)
    {
        var builder = FontSamples.Boxes();
        builder.OmittedTables.Add(tag);

        var exception = Assert.Throws<InvalidImageContentException>(() => FontTestSupport.Load(builder));
        Assert.Contains("'" + tag + "'", exception.Message);
        Assert.Equal(ImageFormat.Unknown, exception.Format);
    }

    [Fact]
    public void AFontWithoutOutlineTableHasEmptyGlyphs()
    {
        var builder = FontSamples.Boxes();
        builder.OmittedTables.Add("glyf");
        builder.OmittedTables.Add("loca");
        var font = FontTestSupport.Load(builder);

        Assert.Equal(13, font.GlyphCount);
        Assert.Equal(1024, font.Face.GetAdvanceWidth(FontSamples.BoxA));
        Assert.Equal("", FontTestSupport.GetOutline(font, FontSamples.BoxA));
    }

    [Fact]
    public void EveryTruncationOfAFontIsRejectedOrGivesAUsableFont()
    {
        // A font is a graph of offsets: a prefix can still hold every table a use needs. Whatever the prefix, loading
        // and reading every glyph throw nothing else than the documented exceptions.
        var data = FontSamples.Boxes().Build();
        var rejected = 0;
        for (var length = 0; length < data.Length; length++)
        {
            try
            {
                var font = Font.Load(data.AsSpan(0, length));
                for (var glyph = 0; glyph < font.GlyphCount; glyph++)
                {
                    FontTestSupport.GetOutline(font, glyph);
                    font.Face.GetAdvanceWidth(glyph);
                }

                for (var codePoint = 0; codePoint < 0x3000; codePoint++)
                {
                    font.Face.MapCodePoint(codePoint);
                }
            }
            catch (Exception exception) when (exception is InvalidImageContentException or UnknownImageFormatException)
            {
                rejected++;
            }
        }

        // The tables are written in the order of their tags, and the last one, post, is optional beyond its header.
        Assert.True(rejected > data.Length - 64, $"Only {rejected} of {data.Length} prefixes were rejected");
    }

    [Theory]
    [InlineData("noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf", null)]
    [InlineData("noto-sans-cjk-variable/NotoSansCJKjp-Variable.subset.otf", "wght=650")]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void DamagedTablesOfVerticalTextAreRejectedOrGiveAUsableFont(string path, string? variations)
    {
        // The tables of vertical text are optional, and are read when a vertical line is set: whatever their bytes,
        // loading the font, reading the vertical metrics of every glyph and setting a line throw nothing else than
        // the documented exceptions.
        var data = File.ReadAllBytes(FixtureRoot.GetDirectory() / "fonts" / path);
        var random = new Random(20261013);
        var options = new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, MaxHeight = 60 };
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        var damaged = 0;
        var usable = 0;
        for (var table = 0; table < tableCount; table++)
        {
            var record = 12 + (table * 16);
            var tag = Encoding.ASCII.GetString(data, record, 4);
            if (tag is not ("vhea" or "vmtx" or "VORG" or "VVAR" or "BASE"))
                continue;

            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
            for (var iteration = 0; iteration < 150; iteration++)
            {
                // A few bytes of the table, most often of its header, or a table that ends early
                var copy = (byte[])data.Clone();
                if (iteration % 5 == 4)
                {
                    BinaryPrimitives.WriteUInt32BigEndian(copy.AsSpan(record + 12), (uint)random.Next(length));
                }
                else
                {
                    for (var i = random.Next(1, 4); i > 0; i--)
                    {
                        copy[offset + random.Next(iteration % 2 == 0 ? Math.Min(length, 40) : length)] = (byte)random.Next(256);
                    }
                }

                damaged++;
                try
                {
                    var font = Font.Load(copy);
                    if (variations is not null)
                    {
                        font = FontTestSupport.WithVariations(font, variations);
                    }

                    for (var glyph = 0; glyph < font.GlyphCount; glyph++)
                    {
                        font.Face.GetAdvanceHeight(glyph);
                        font.Face.GetVerticalOrigin(glyph, scratch: null, out _, out _);
                    }

                    var layout = TextLayout.Create("\u300C\u65E5\u672C\u300D\u3001\u30FC ABC \u6771\u4EAC\u3002", new TextStyle(font, 20), options);
                    Assert.True(layout.LineCount >= 1);
                    Assert.True(double.IsFinite(layout.Width) && double.IsFinite(layout.Height));
                    usable++;
                }
                catch (Exception exception) when (exception is ImageException or FontException)
                {
                }
            }
        }

        // Every table of vertical text the font has was damaged, and a font is still a font without them
        Assert.True(damaged >= 450, $"Only {damaged} fonts were damaged");
        Assert.True(usable > damaged / 2, $"Only {usable} of {damaged} damaged fonts could be used");
    }

    [Theory]
    [InlineData("head", 12, "magic number")]
    [InlineData("head", 18, "units per em")]
    [InlineData("head", 50, "location format")]
    [InlineData("maxp", 4, "no glyph")]
    [InlineData("hhea", 34, "horizontal metrics")]
    public void AnInvalidHeaderFieldIsRejected(string tag, int offset, string expectedMessage)
    {
        var tables = FontSamples.Boxes().BuildTables();
        var table = (byte[])tables[tag].Clone();

        // Zero the 16-bit field, or set an out-of-range value where zero is valid.
        table[offset] = 0;
        table[offset + 1] = (byte)(tag == "head" && offset == 50 ? 7 : 0);
        tables[tag] = table;

        var exception = Assert.Throws<InvalidImageContentException>(() => Font.Load(FontBuilder.WriteSfnt(tables)));
        Assert.Contains(expectedMessage, exception.Message);
    }

    [Fact]
    public void MalformedGlyphDataIsRejectedWhenTheGlyphIsDecoded()
    {
        // The font loads: the data of a glyph is only read when the glyph is needed.
        static Font LoadWithGlyph(byte[] data)
        {
            var builder = new FontBuilder { CmapFormats = FontCmapFormats.Format4 };
            builder.AddGlyph(FontGlyph.Empty(500));
            builder.AddGlyph(new FontGlyph { AdvanceWidth = 500, RawData = data, BoundsOverride = new FontBounds(0, 0, 10, 10) }, 'a');
            return FontTestSupport.Load(builder);
        }

        // One contour whose last point is the point 3, no instruction, then only two flags.
        byte[] truncated = [0, 1, 0, 0, 0, 0, 0, 10, 0, 10, 0, 3, 0, 0, 1, 1];
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(LoadWithGlyph(truncated), 1));

        // Two contours that end at the points 3 then 2.
        byte[] decreasing = [0, 2, 0, 0, 0, 0, 0, 10, 0, 10, 0, 3, 0, 2, 0, 0, 1, 1, 1, 1];
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(LoadWithGlyph(decreasing), 1));

        // A flag repeated more times than there are points.
        byte[] tooManyFlags = [0, 1, 0, 0, 0, 0, 0, 10, 0, 10, 0, 1, 0, 0, 0x09, 5, 0, 0, 0, 0];
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(LoadWithGlyph(tooManyFlags), 1));

        // Instructions longer than the glyph.
        byte[] longInstructions = [0, 1, 0, 0, 0, 0, 0, 10, 0, 10, 0, 0, 0xFF, 0xFF, 1];
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(LoadWithGlyph(longInstructions), 1));

        // A glyph shorter than its header.
        Assert.Throws<InvalidImageContentException>(() => FontTestSupport.GetOutline(LoadWithGlyph([0, 1]), 1));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AWoffFileIsExpandedToItsFont(bool compress)
    {
        var builder = FontSamples.Boxes();
        var original = builder.Build();
        var font = Font.Load(FontBuilder.WriteWoff(original, compress));

        Assert.Equal("Meziantou Boxes", font.FamilyName);
        AssertSameFont(Font.Load(original), font, sameGlyphBytes: true);

        // The expanded font is the original one, table for table; only the unused checksum fields may differ.
        Assert.HasCount(original.Length, font.Face.Data.RawBytes);
    }

    [Fact]
    public void AMalformedWoffFileIsRejected()
    {
        var woff = FontBuilder.WriteWoff(FontSamples.Boxes().Build());
        const int Directory = 44;

        // The glyf table is the third one (OS/2, cmap, glyf) and is stored compressed.
        const int Glyf = Directory + (2 * 20);
        Assert.Equal("glyf"u8, woff.AsSpan(Glyf, 4));
        var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(Glyf + 4));
        var compressedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(Glyf + 8));
        var originalLength = (int)BinaryPrimitives.ReadUInt32BigEndian(woff.AsSpan(Glyf + 12));
        Assert.True(compressedLength < originalLength);

        byte[] Edit(Action<byte[]> edit)
        {
            var copy = (byte[])woff.Clone();
            edit(copy);
            return copy;
        }

        // A table declared longer or shorter than what its data expands to.
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Glyf + 12), (uint)originalLength + 4)), "glyf");
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Glyf + 12), (uint)originalLength - 4)), "glyf");

        // Compressed data cut before its end, or without its checksum, or with a wrong checksum.
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Glyf + 8), (uint)compressedLength / 2)), "glyf");
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Glyf + 8), (uint)compressedLength - 4)), "glyf");
        AssertInvalid(Edit(data => data[offset + compressedLength - 1] ^= 0x01), "glyf");

        // Data that is not a zlib stream, and a zlib stream that asks for a preset dictionary (the header 78 BB), which
        // the decompressor reports as an I/O error.
        AssertInvalid(Edit(data => data.AsSpan(offset, compressedLength).Fill(0xAA)), "glyf");
        AssertInvalid(Edit(data => new byte[] { 0x78, 0xBB }.CopyTo(data.AsSpan(offset))), "glyf");

        // A table longer compressed than expanded, and a table outside the file.
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Glyf + 12), (uint)compressedLength - 1)), "glyf");
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(Glyf + 4), (uint)data.Length)), "glyf");

        // A collection, a file inside a file, a directory longer than the file.
        AssertInvalid(Edit(data => "ttcf"u8.CopyTo(data.AsSpan(4))), "collection");
        AssertInvalid(Edit(data => "wOFF"u8.CopyTo(data.AsSpan(4))), "another WOFF");
        AssertInvalid(Edit(data => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(12), 5000)), "truncated");
        Assert.Throws<InvalidImageContentException>(() => Font.Load(woff.AsSpan(0, 20)));

        static void AssertInvalid(byte[] data, string expectedMessage)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Font.Load(data));
            Assert.Contains(expectedMessage, exception.Message);
            Assert.Equal(ImageFormat.Unknown, exception.Format);
        }
    }

    [Fact]
    public void TheFontOfAContainerIsLimitedBeforeItIsExpanded()
    {
        var builder = FontSamples.Boxes();
        var sfntLength = builder.Build().Length;
        static FontLoadOptions Options(long fontBytes) => new() { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFontBytes = fontBytes } } };

        // WOFF: the font is exactly the original one, and smaller in the file.
        var woff = FontBuilder.WriteWoff(builder.Build());
        Assert.HasCountLessThan(sfntLength, woff);
        Assert.Equal(13, Font.Load(woff, Options(sfntLength)).GlyphCount);
        var exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(woff, Options(sfntLength - 1)));
        Assert.Equal(ImageResourceLimitKind.FontBytes, exception.Kind);
        Assert.Equal(sfntLength - 1, exception.Limit);
        Assert.Equal(sfntLength, exception.Requested);

        // A table that declares four gigabytes is refused without being expanded.
        var huge = (byte[])woff.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(huge.AsSpan(44 + (2 * 20) + 12), uint.MaxValue);
        exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(huge));
        Assert.Equal(ImageResourceLimitKind.FontBytes, exception.Kind);
        Assert.Equal(ImageResourceLimits.DefaultMaxFontBytes, exception.Limit);

        // WOFF2: the rebuilt font has its glyphs padded to four bytes.
        var woff2 = FontWoff2Writer.Write(builder);
        var rebuiltLength = Font.Load(woff2).Face.Data.RawBytes.Length;
        Assert.Equal(13, Font.Load(woff2, Options(rebuiltLength)).GlyphCount);
        exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(woff2, Options(rebuiltLength - 1)));
        Assert.Equal(ImageResourceLimitKind.FontBytes, exception.Kind);
        Assert.Equal(rebuiltLength, exception.Requested);

        // The tables of a WOFF2 file are expanded in temporary storage, which has its own limit.
        var small = new FontLoadOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = 64 } } };
        exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(woff2, small));
        Assert.Equal(ImageResourceLimitKind.LiveAllocationBytes, exception.Kind);

        // Tables that declare more than a font can be are refused before the stream is expanded.
        var tables = FontWoff2Writer.CreateTables(builder, new FontWoff2Options());
        tables[0] = tables[0] with { OriginalLength = int.MaxValue, Data = [] };
        tables.Add(tables[0] with { Tag = "TST1" });
        exception = Assert.Throws<ImageResourceLimitException>(() => Font.Load(FontWoff2Writer.WriteContainer(0x00010000, tables)));
        Assert.Equal(ImageResourceLimitKind.FontBytes, exception.Kind);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, false, 2)]
    [InlineData(true, true, 3)]
    [InlineData(false, false, 3)]
    public void AWoff2FileIsRebuiltToItsFont(bool transformGlyphs, bool explicitBoxes, int metricsFlags)
    {
        var options = new FontWoff2Options { TransformGlyphs = transformGlyphs, ExplicitBoundingBoxes = explicitBoxes, HorizontalMetricsFlags = metricsFlags };
        foreach (var create in (Func<FontBuilder>[])[CreateBoxesWithSharedAdvances, FontSamples.Curves, FontSamples.Deltas])
        {
            var builder = create();

            // A table the format has no short code for.
            builder.RawTables["TEST"] = [1, 2, 3, 4, 5];
            var expected = FontTestSupport.Load(builder);
            var actual = Font.Load(FontWoff2Writer.Write(builder, options));

            AssertSameFont(expected, actual, sameGlyphBytes: !transformGlyphs);
            Assert.True(actual.Face.TryGetTable(0x54455354, out var custom));
            Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, custom.ToArray());
        }

        static FontBuilder CreateBoxesWithSharedAdvances()
        {
            // Trailing glyphs that share an advance: the horizontal metrics then have their two parts.
            var builder = FontSamples.Boxes();
            builder.CompactHorizontalMetrics = true;
            builder.AddGlyph(FontGlyph.Rectangle(600, 30, 0, 100, 100));
            builder.AddGlyph(FontGlyph.Empty(600));
            builder.AddGlyph(FontGlyph.Rectangle(600, -20, 0, 100, 100));
            return builder;
        }
    }

    [Fact]
    public void AMalformedWoff2FileIsRejected()
    {
        // The transformed glyph table: a header of 36 bytes with the lengths of seven streams, then the streams.
        static int StreamOffset(byte[] table, int stream)
        {
            var offset = 36;
            for (var i = 0; i < stream; i++)
            {
                offset += (int)BinaryPrimitives.ReadUInt32BigEndian(table.AsSpan(8 + (i * 4)));
            }

            return offset;
        }

        const int ContourCounts = 0;
        const int PointCounts = 1;
        const int Boxes = 5;

        static byte[] Write(Func<List<FontWoff2Table>, IEnumerable<FontWoff2Table>> edit, FontWoff2Options? options = null)
            => FontWoff2Writer.WriteContainer(0x00010000, [.. edit(FontWoff2Writer.CreateTables(FontSamples.Boxes(), options ?? new FontWoff2Options()))]);

        static byte[] EditGlyphs(Action<byte[]> edit)
        {
            return Write(tables => tables.Select(table =>
            {
                if (table.Tag != "glyf")
                    return table;

                var data = (byte[])table.Data.Clone();
                edit(data);
                return table with { Data = data };
            }));
        }

        // The file as written is valid.
        Assert.Equal(13, Font.Load(Write(tables => tables)).GlyphCount);

        // The glyph 1 is a space, the glyph 2 a square, the glyph 10 a composite.
        AssertInvalid(EditGlyphs(data => BinaryPrimitives.WriteInt16BigEndian(data.AsSpan(StreamOffset(data, ContourCounts) + (2 * 2)), -2)), "-2 contours");
        AssertInvalid(EditGlyphs(data => data[StreamOffset(data, Boxes)] |= 0x40), "without outline");
        AssertInvalid(EditGlyphs(data => data[StreamOffset(data, Boxes) + 1] &= 0xDF), "composite glyph");
        AssertInvalid(EditGlyphs(data => data[StreamOffset(data, PointCounts)] = 0), "contour without point");
        AssertInvalid(EditGlyphs(data => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 2)), "location format 2");
        AssertInvalid(EditGlyphs(data => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(6), 1)), "disagree");
        AssertInvalid(EditGlyphs(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), 4)), "truncated");
        AssertInvalid(EditGlyphs(data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(32), uint.MaxValue)), "truncated");
        AssertInvalid(EditGlyphs(data => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(4), 200)), "truncated");

        // The transformations the format does not define, or applies to other tables.
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "cmap" ? table with { Version = 1 } : table)), "transformation 1");
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "hmtx" ? table with { Version = 2 } : table)), "transformation 2");
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "glyf" ? table with { Version = 1 } : table)), "transformation 1");
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "loca" ? table with { Data = [0, 0] } : table)), "always empty");
        AssertInvalid(Write(tables => tables.Where(table => table.Tag != "loca")), "not transformed together");
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "loca" ? table with { Version = 3, Data = new byte[28] } : table)), "not transformed together");
        AssertInvalid(Write(tables => tables.Append(tables.Single(table => table.Tag == "head"))), "listed twice");

        // Transformed horizontal metrics: flags that omit nothing or that are not defined, missing data, missing tables.
        var metrics = new FontWoff2Options { HorizontalMetricsFlags = 3 };
        Assert.Equal(13, Font.Load(Write(tables => tables, metrics)).GlyphCount);
        AssertInvalid(Write(tables => tables, new FontWoff2Options { HorizontalMetricsFlags = 4 }), "flags 0x04");
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "hmtx" ? table with { Data = [0, .. table.Data.AsSpan(1)] } : table), metrics), "flags 0x00");
        AssertInvalid(Write(tables => tables.Select(table => table.Tag == "hmtx" ? table with { Data = table.Data[..^1] } : table), metrics), "truncated");
        AssertInvalid(Write(tables => tables.Where(table => table.Tag != "hhea"), metrics), "does not have the tables");
        AssertInvalid(Write(tables => tables.Where(table => table.Tag != "maxp"), metrics), "does not have the tables");

        // The container: no table, a compressed stream that is cut, too long, too short, or not a Brotli stream.
        var valid = Write(tables => tables);
        AssertInvalid(Write(_ => []), "no table");
        var compressedLength = (int)BinaryPrimitives.ReadUInt32BigEndian(valid.AsSpan(20));
        var streamOffset = valid.Length - ((compressedLength + 3) & ~3);
        AssertInvalid(Edit(valid, data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)compressedLength - 10)), "WOFF2");
        AssertInvalid(Edit(valid, data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(20), (uint)compressedLength + 100)), "truncated");
        AssertInvalid(Edit(valid, data => data.AsSpan(streamOffset, compressedLength).Fill(0xFF)), "WOFF2");

        // The first entry of the directory is the OS/2 table: a known tag and a length of 96, in one byte.
        Assert.Equal((byte)96, valid[49]);
        AssertInvalid(Edit(valid, data => data[49] = 95), "longer than its tables");
        AssertInvalid(Edit(valid, data => data[49] = 97), "WOFF2");

        // A length with a leading zero, or of more than 32 bits.
        AssertInvalid(WithFileLength([.. valid.AsSpan(0, 49), 0x80, 96, .. valid.AsSpan(50)]), "malformed length");
        AssertInvalid(WithFileLength([.. valid.AsSpan(0, 49), 0x9F, 0xFF, 0xFF, 0xFF, 0x7F, .. valid.AsSpan(50)]), "malformed length");
        AssertInvalid(WithFileLength([.. valid.AsSpan(0, 49), 0x81, 0x80, 0x80, 0x80, 0x80, 0x00, .. valid.AsSpan(50)]), "malformed length");
        Assert.Throws<InvalidImageContentException>(() => Font.Load(valid.AsSpan(0, 30)));

        // The header gives the size of the file, and the file ends with its last block.
        AssertInvalid(Edit(valid, data => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)data.Length - 4)), "another length than its size");
        AssertInvalid(WithFileLength([.. valid, 0, 0, 0, 0]), "after its last block");

        static byte[] WithFileLength(byte[] data)
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), (uint)data.Length);
            return data;
        }

        static byte[] Edit(byte[] data, Action<byte[]> edit)
        {
            var copy = (byte[])data.Clone();
            edit(copy);
            return copy;
        }

        static void AssertInvalid(byte[] data, string expectedMessage)
        {
            var exception = Assert.Throws<InvalidImageContentException>(() => Font.Load(data));
            Assert.Contains(expectedMessage, exception.Message);
            Assert.Equal(ImageFormat.Unknown, exception.Format);
        }
    }

    [Fact]
    public void EveryTruncationOfAContainerIsRejected()
    {
        // A container is read whole, then expanded: a prefix never gives a font.
        foreach (var data in (byte[][])[FontBuilder.WriteWoff(FontSamples.Boxes().Build()), FontWoff2Writer.Write(FontSamples.Boxes(), new FontWoff2Options { HorizontalMetricsFlags = 3 })])
        {
            // The files end with up to three bytes of padding.
            for (var length = 0; length < data.Length - 3; length++)
            {
                try
                {
                    Font.Load(data.AsSpan(0, length));
                    Assert.Fail($"The first {length} bytes of a container of {data.Length} bytes were accepted");
                }
                catch (Exception exception) when (exception is InvalidImageContentException or UnknownImageFormatException)
                {
                }
            }
        }
    }

    /// <summary>Asserts that two fonts have the same tables, or the same glyphs when the glyph tables were written again.</summary>
    [Fact]
    public void AFontExposesTheFieldsOfItsHeaderTables()
    {
        var builder = FontSamples.Boxes();
        builder.WidthClass = 3;
        builder.SubscriptYOffset = 150;
        builder.SuperscriptYOffset = 350;
        builder.ItalicAngle = -12.5;
        builder.IsItalic = true;
        var face = FontTestSupport.Load(builder).Face;

        Assert.Equal(3, face.HeadFlags);
        Assert.Equal(2, face.MacStyle);
        Assert.Equal((-640, -192, 1024, 960), (face.XMin, face.YMin, face.XMax, face.YMax));
        Assert.Equal(4, face.Os2Version);
        Assert.Equal(670, face.AverageCharWidth);
        Assert.Equal(3, face.WidthClass);
        Assert.Equal(0x0001, face.Selection);
        Assert.Equal(150, face.SubscriptYOffset);
        Assert.Equal(350, face.SuperscriptYOffset);
        Assert.Equal(-12.5, face.ItalicAngle);
        Assert.False(face.HasVerticalMetrics);
        Assert.Equal((0, 0, 0), (face.VerticalAscender, face.VerticalDescender, face.VerticalLineGap));

        // A font without OS/2 table has the default width class.
        builder = FontSamples.Boxes();
        builder.OmitOs2 = true;
        face = FontTestSupport.Load(builder).Face;
        Assert.Equal((0, 5, 0, 0), (face.Os2Version, face.WidthClass, face.Selection, face.SubscriptYOffset));
    }

    [Fact]
    public void TheSideBearingsOfTheGlyphsAreThoseOfTheMetricsTables()
    {
        var face = FontTestSupport.Load(FontSamples.Boxes()).Face;
        Assert.Equal(128, face.GetLeftSideBearing(FontSamples.BoxA));
        Assert.Equal(64, face.GetLeftSideBearing(FontSamples.BoxB));
        Assert.Equal(0, face.GetLeftSideBearing(FontSamples.BoxC));
        Assert.Equal(0, face.GetLeftSideBearing(-1));
        Assert.Equal(0, face.GetLeftSideBearing(face.GlyphCount));
        Assert.Equal(0, face.GetTopSideBearing(FontSamples.BoxA));

        // The glyphs after the last metric have their bearing alone.
        var compact = FontSamples.Boxes();
        compact.CompactHorizontalMetrics = true;
        face = FontTestSupport.Load(compact).Face;
        Assert.Equal(64, face.GetLeftSideBearing(FontSamples.HyphenGlyph));
        Assert.Equal(128, face.GetLeftSideBearing(FontSamples.BoxA));
    }

    [Fact]
    public void TheBoxOfAGlyphIsTheOneTheFontDeclaresOrTheOneOfItsOutline()
    {
        var face = FontTestSupport.Load(FontSamples.Boxes()).Face;
        using var scratch = new GlyphScratch(AllocationScope.Create(configuration: null, "FontTests"));
        Assert.True(face.TryGetGlyphBounds(FontSamples.BoxA, scratch, out var bounds));
        Assert.Equal(new GlyphBox(128, 0, 896, 768), bounds);
        Assert.True(face.TryGetGlyphBounds(11, scratch, out bounds));
        Assert.Equal(new GlyphBox(160, 0, 736, 768), bounds);
        Assert.False(face.TryGetGlyphBounds(FontSamples.SpaceGlyph, scratch, out _));
        Assert.False(face.TryGetGlyphBounds(face.GlyphCount, scratch, out _));

        // A CFF outline has no declared box: the box of its points, rounded outward.
        var cff = new FontCffWriter();
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.RMoveTo, 10, 20).Op(FontCharstring.RLineTo, 30, 0).Op(FontCharstring.RLineTo, 0, 40).Op(FontCharstring.EndChar).ToArray());
        cff.CharStrings.Add(new FontCharstring().Op(FontCharstring.EndChar).ToArray());
        face = LoadCff(cff).Face;
        Assert.True(face.TryGetGlyphBounds(0, scratch, out bounds));
        Assert.Equal(new GlyphBox(10, 20, 40, 60), bounds);
        Assert.False(face.TryGetGlyphBounds(1, scratch, out _));
    }

    [Fact]
    public void TheLayersOfAColorGlyphOfVersion0AreItsRecords()
    {
        var face = FontTestSupport.Load(FontSamples.ColorBoxes()).Face;
        Assert.Equal([new ColorLayer(3, 0), new ColorLayer(4, 1), new ColorLayer(5, CpalTable.Foreground)], face.GetColorLayers(FontSamples.ColorA));
        Assert.Equal([new ColorLayer(7, 2), new ColorLayer(8, 3)], face.GetColorLayers(FontSamples.ColorB));
        Assert.Null(face.GetColorLayers(FontSamples.SpaceGlyph));
        Assert.Null(face.GetColorLayers(-1));
        Assert.Null(FontTestSupport.Load(FontSamples.Boxes()).Face.GetColorLayers(FontSamples.BoxA));
    }

    [Fact]
    public void TheMathTableGivesTheConstantsAndTheGlyphsOfMathematicalLayout()
    {
        var builder = FontSamples.Boxes();
        builder.RawTables["MATH"] = CreateMathTable(deviceTable: false);
        var face = FontTestSupport.Load(builder).Face;

        Assert.True(face.HasMathTable);
        Assert.Equal(70, face.GetMathConstant(MathConstant.ScriptPercentScaleDown));
        Assert.Equal(50, face.GetMathConstant(MathConstant.ScriptScriptPercentScaleDown));
        Assert.Equal(1500, face.GetMathConstant(MathConstant.DelimitedSubFormulaMinHeight));
        Assert.Equal(1200, face.GetMathConstant(MathConstant.DisplayOperatorMinHeight));
        Assert.Equal(100, face.GetMathConstant(MathConstant.MathLeading));
        Assert.Equal(250, face.GetMathConstant(MathConstant.AxisHeight));
        Assert.Equal(150, face.GetMathConstant(MathConstant.RadicalKernAfterDegree));
        Assert.Equal(60, face.GetMathConstant(MathConstant.RadicalDegreeBottomRaisePercent));

        Assert.True(face.TryGetMathItalicsCorrection(FontSamples.BoxA, out var italicsCorrection));
        Assert.Equal(40, italicsCorrection);
        Assert.False(face.TryGetMathItalicsCorrection(FontSamples.BoxB, out _));
        Assert.True(face.TryGetMathTopAccentAttachment(FontSamples.BoxA, out var accent));
        Assert.Equal(300, accent);
        Assert.True(face.IsMathExtendedShape(FontSamples.BoxC));
        Assert.False(face.IsMathExtendedShape(FontSamples.BoxA));
        Assert.Equal(20, face.MathMinConnectorOverlap);

        var construction = face.GetMathGlyphConstruction(FontSamples.BoxB, isVertical: true);
        Assert.NotNull(construction);
        Assert.Equal([new MathGlyphVariant(3, 512), new MathGlyphVariant(4, 1024)], construction.Variants);
        Assert.Equal([new MathGlyphPart(5, 0, 50, 300, IsExtender: false), new MathGlyphPart(6, 50, 50, 400, IsExtender: true)], construction.AssemblyParts);
        Assert.Equal(7, construction.AssemblyItalicsCorrection);
        Assert.Null(face.GetMathGlyphConstruction(FontSamples.BoxB, isVertical: false));
        Assert.Null(face.GetMathGlyphConstruction(FontSamples.BoxA, isVertical: true));

        // A font without the table, and a table of an unknown version, have no mathematical data.
        face = FontTestSupport.Load(FontSamples.Boxes()).Face;
        Assert.False(face.HasMathTable);
        Assert.Equal(0, face.GetMathConstant(MathConstant.AxisHeight));
        Assert.Null(face.GetMathGlyphConstruction(FontSamples.BoxB, isVertical: true));
        builder = FontSamples.Boxes();
        var unknown = CreateMathTable(deviceTable: false);
        unknown[1] = 2;
        builder.RawTables["MATH"] = unknown;
        Assert.False(FontTestSupport.Load(builder).Face.HasMathTable);
    }

    [Fact]
    public void TheValuesOfTheMathTableVaryWithTheInstance()
    {
        // A variation index device table on the axis height, whose delta is in the store of the GDEF table.
        var builder = FontSamples.Variable();
        builder.RawTables["MATH"] = CreateMathTable(deviceTable: true);
        var store = new FontVariationStore();
        store.Regions.Add(new FontVariationRegion((0, 1, 1), (0, 0, 0)));
        var items = new FontVariationSubtable { Regions = [0] };
        items.Items.Add([100]);
        store.Subtables.Add(items);
        var gdef = new FontWriter();
        gdef.UInt16(1);
        gdef.UInt16(3);
        gdef.Zeros(10);
        gdef.UInt32(18);
        var gdefHeader = gdef.ToArray();
        builder.RawTables["GDEF"] = [.. gdefHeader, .. FontVariationWriter.WriteStore(store, builder.Axes.Count)];
        var font = FontTestSupport.Load(builder);

        Assert.Equal(250, font.Face.GetMathConstant(MathConstant.AxisHeight));
        Assert.Equal(350, FontTestSupport.WithVariations(font, "wght=900").Face.GetMathConstant(MathConstant.AxisHeight));
    }

    [Fact]
    public void TheSubscriptAndSuperscriptOffsetsVaryWithTheInstance()
    {
        var builder = FontSamples.Variable();
        builder.SubscriptYOffset = 150;
        builder.SuperscriptYOffset = 350;
        builder.WidthClass = 5;
        var items = builder.MetricVariations!.Subtables[0].Items;
        builder.MetricVariationItems["sbyo"] = (0, items.Count);
        items.Add([30, -10]);
        builder.MetricVariationItems["spyo"] = (0, items.Count);
        items.Add([-40, 0]);
        var font = FontTestSupport.Load(builder);

        Assert.Equal((150, 350), (font.Face.SubscriptYOffset, font.Face.SuperscriptYOffset));
        var bold = FontTestSupport.WithVariations(font, "wght=900").Face;
        Assert.Equal((180, 310), (bold.SubscriptYOffset, bold.SuperscriptYOffset));
    }

    /// <summary>
    /// Creates a MATH table: constants (the value records are 100 and more, the axis height 250), an italics correction
    /// and a top accent attachment for A, C as an extended shape, and the vertical construction of B (two variants and
    /// an assembly of two parts).
    /// </summary>
    private static byte[] CreateMathTable(bool deviceTable)
    {
        var table = new byte[deviceTable ? 328 : 322];
        void Write(int position, int value) => BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(position), (ushort)value);

        // Header: version 1.0, MathConstants at 10, MathGlyphInfo at 224, MathVariants at 266.
        Write(0, 1);
        Write(4, 10);
        Write(6, 224);
        Write(8, 266);

        // MathConstants: the four scalars, 51 value records, then the raise of the degree of a radical.
        Write(10, 70);
        Write(12, 50);
        Write(14, 1500);
        Write(16, 1200);
        for (var i = 0; i < 51; i++)
        {
            Write(18 + (i * 4), i == 1 ? 250 : 100 + i);
        }

        Write(222, 60);
        if (deviceTable)
        {
            // The device table of the axis height, from the start of MathConstants: a variation index (0, 0).
            Write(24, 322 - 10);
            Write(322, 0);
            Write(324, 0);
            Write(326, 0x8000);
        }

        // MathGlyphInfo: italics corrections at +8, top accent attachments at +22, extended shapes at +36.
        Write(224, 8);
        Write(226, 22);
        Write(228, 36);
        Write(232, 8);
        Write(234, 1);
        Write(236, 40);
        WriteCoverage(240, FontSamples.BoxA);
        Write(246, 8);
        Write(248, 1);
        Write(250, 300);
        WriteCoverage(254, FontSamples.BoxA);
        WriteCoverage(260, FontSamples.BoxC);

        // MathVariants: the minimum overlap, the vertical coverage (B), one vertical construction.
        Write(266, 20);
        Write(268, 12);
        Write(272, 1);
        Write(276, 18);
        WriteCoverage(278, FontSamples.BoxB);

        // MathGlyphConstruction: the assembly at +12, two variants.
        Write(284, 12);
        Write(286, 2);
        Write(288, 3);
        Write(290, 512);
        Write(292, 4);
        Write(294, 1024);

        // GlyphAssembly: the italics correction, two parts.
        Write(296, 7);
        Write(300, 2);
        Write(302, 5);
        Write(306, 50);
        Write(308, 300);
        Write(312, 6);
        Write(314, 50);
        Write(316, 50);
        Write(318, 400);
        Write(320, 1);
        return table;

        void WriteCoverage(int position, int glyph)
        {
            Write(position, 1);
            Write(position + 2, 1);
            Write(position + 4, glyph);
        }
    }

    private static void AssertSameFont(Font expected, Font actual, bool sameGlyphBytes)
    {
        Assert.Equal(expected.Face.TableTags.ToArray(), actual.Face.TableTags.ToArray());
        foreach (var tag in expected.Face.TableTags)
        {
            if (!sameGlyphBytes && tag is FontTags.Glyf or FontTags.Loca)
                continue;

            Assert.True(expected.Face.TryGetTable(tag, out var expectedTable));
            Assert.True(actual.Face.TryGetTable(tag, out var actualTable));
            var expectedBytes = expectedTable.ToArray();
            var actualBytes = actualTable.ToArray();
            if (tag == FontTags.Head)
            {
                // The checksum of the whole file, which depends on how the file is laid out.
                expectedBytes.AsSpan(8, 4).Clear();
                actualBytes.AsSpan(8, 4).Clear();
            }

            Assert.True(expectedBytes.AsSpan().SequenceEqual(actualBytes), $"The '{FontTags.Format(tag)}' table differs");
        }

        Assert.Equal(expected.GlyphCount, actual.GlyphCount);
        for (var glyph = 0; glyph < expected.GlyphCount; glyph++)
        {
            Assert.Equal(expected.Face.GetAdvanceWidth(glyph), actual.Face.GetAdvanceWidth(glyph));
            Assert.Equal(FontTestSupport.GetOutline(expected, glyph), FontTestSupport.GetOutline(actual, glyph));
        }
    }

    [Fact]
    public void AFontCanBeUsedByManyThreads()
    {
        var font = FontTestSupport.Load(FontSamples.Curves());
        var expected = Enumerable.Range(0, font.GlyphCount).Select(glyph => FontTestSupport.GetOutline(font, glyph)).ToArray();

        Parallel.For(0, 64, _ =>
        {
            for (var glyph = 0; glyph < font.GlyphCount; glyph++)
            {
                if (!string.Equals(expected[glyph], FontTestSupport.GetOutline(font, glyph), StringComparison.Ordinal))
                    throw new InvalidOperationException("A glyph was decoded differently by another thread.");
            }
        });
    }

    [Fact]
    public void SystemFontsAreIndexedWithoutLoadingThem()
    {
        using var directory = new TemporaryFontDirectory();
        var bold = FontSamples.Boxes();
        bold.FamilyName = "Meziantou Boxes Bold";
        bold.TypographicFamilyName = "Meziantou Boxes";
        bold.TypographicSubfamilyName = "Bold";
        bold.PostScriptName = "MeziantouBoxes-Bold";
        bold.WeightClass = 700;
        bold.IsBold = true;
        bold.WidthClass = 6;
        bold.ItalicAngle = -12;
        bold.IsMonospaced = true;
        var variable = FontSamples.Curves();
        variable.Axes.Add(new FontAxisDefinition("wght", 100, 400, 900, "Weight"));
        var italic = FontSamples.Boxes();
        italic.SubfamilyName = "Italic";
        italic.IsItalic = true;
        italic.PostScriptName = "MeziantouBoxes-Italic";

        var regularPath = directory.Write("boxes.ttf", FontSamples.Boxes().Build());
        var boldPath = directory.Write(Path.Combine("nested", "deeper", "boxes-bold.otf"), bold.Build());
        var collectionPath = directory.Write("family.TTC", FontBuilder.WriteCollection([variable.BuildTables(), italic.BuildTables()], 2));
        var woff2Path = directory.Write("boxes.woff2", FontWoff2Writer.Write(FontSamples.Curves()));
        directory.Write("notes.txt", "not a font"u8.ToArray());
        directory.Write("boxes.bin", FontSamples.Boxes().Build());
        directory.Write("truncated.ttf", FontSamples.Boxes().Build()[..200]);
        directory.Write("garbage.otf", "neither a font"u8.ToArray());
        directory.Write("empty.ttf", []);

        var collection = SystemFontCollection.Create([directory.Path]);

        Assert.Equal(
            new[] { (regularPath, 0), (collectionPath, 1), (boldPath, 0), (woff2Path, 0), (collectionPath, 0) },
            collection.Fonts.Select(font => (font.FilePath, font.FaceIndex)).ToArray());

        // What the index declares is what the loaded face declares.
        var environment = new PooledFontEnvironment(FontResourceLimits.Default, 1024 * 1024);
        foreach (var descriptor in collection.Fonts)
        {
            var face = FontLoader.Load(File.ReadAllBytes(descriptor.FilePath), environment)[descriptor.FaceIndex];
            Assert.Equal(face.FamilyName, descriptor.FamilyName);
            Assert.Equal(face.SubfamilyName, descriptor.SubfamilyName);
            Assert.Equal(face.PostScriptName, descriptor.PostScriptName);
            Assert.Equal(face.GetName(4), descriptor.FullName);
            Assert.Equal(face.HasOs2, descriptor.HasOs2);
            Assert.Equal(face.Weight, descriptor.Weight);
            Assert.Equal(face.WidthClass, descriptor.WidthClass);
            Assert.Equal(face.Selection, descriptor.Selection);
            Assert.Equal(face.MacStyle, descriptor.MacStyle);
            Assert.Equal(face.IsItalic, descriptor.IsItalic);
            Assert.Equal(face.ItalicAngle, descriptor.ItalicAngle);
            Assert.Equal(face.IsMonospaced, descriptor.IsMonospaced);
            Assert.Equal(face.Axes.ToArray(), descriptor.Axes.ToArray());
            Assert.Equal(face.FamilyName, FontCollection.Load(descriptor.FilePath)[descriptor.FaceIndex].FamilyName);
        }

        var boldDescriptor = collection.Fonts[2];
        Assert.Equal(new[] { "Meziantou Boxes", "Meziantou Boxes Bold" }, boldDescriptor.FamilyNames.ToArray());
        Assert.Equal("Bold", boldDescriptor.SubfamilyName);
        Assert.Equal(700, boldDescriptor.Weight);
        Assert.Equal(6, boldDescriptor.WidthClass);
        Assert.Equal(-12, boldDescriptor.ItalicAngle);
        Assert.True(boldDescriptor.IsMonospaced);
        Assert.False(boldDescriptor.IsItalic);
        Assert.False(boldDescriptor.IsOblique);
        Assert.Equal("Meziantou Boxes Bold Regular", boldDescriptor.FullName);
        Assert.True(collection.Fonts[1].IsItalic);
        Assert.Equal("wght", Encoding.ASCII.GetString(BitConverter.GetBytes(BinaryPrimitives.ReverseEndianness(collection.Fonts[4].Axes[0].Tag))));
        Assert.Contains(regularPath, collection.Fonts[0].ToString());
    }

    [Fact]
    public void SystemFontsAreFoundOnceAndOnlyWhenTheFontCodeLoadsThem()
    {
        using var directory = new TemporaryFontDirectory();
        var path = directory.Write("boxes.ttf", FontSamples.Boxes().Build());

        // A font without outlines nor color glyphs is not loaded by WebPlatform: it is left out.
        var outlineless = FontSamples.Boxes();
        outlineless.OmittedTables.Add("glyf");
        outlineless.OmittedTables.Add("loca");
        directory.Write("outlineless.ttf", outlineless.Build());

        // A collection is left out whole when one of its faces is malformed.
        var collection = FontBuilder.WriteCollection([FontSamples.Boxes().BuildTables(), FontSamples.Curves().BuildTables()]);
        BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(16), 0xFFFFFF);
        directory.Write("broken.ttc", collection);

        var fonts = SystemFontCollection.Create([directory.Path, directory.Path + Path.DirectorySeparatorChar, path, Path.Combine(directory.Path, "missing"), ""]);

        Assert.Equal(path, Assert.Single(fonts.Fonts).FilePath);
        Assert.Empty(SystemFontCollection.Create([]).Fonts);
        Assert.Throws<ArgumentNullException>("paths", () => SystemFontCollection.Create(null!));
    }

    [Fact]
    public void SystemFontsAreFoundByAnyFamilyNameAndByTheirFullOrPostScriptName()
    {
        using var directory = new TemporaryFontDirectory();
        var light = FontSamples.Boxes();
        light.FamilyName = "Meziantou Boxes Light";
        light.TypographicFamilyName = "Meziantou Boxes";
        light.TypographicSubfamilyName = "Light";
        light.WeightClass = 300;
        light.PostScriptName = "MeziantouBoxes-Light";
        light.RawTables["name"] = WriteNameTable(
            (3, 1, 0x0409, 1, "Meziantou Boxes Light"),
            (3, 1, 0x0409, 2, "Regular"),
            (3, 1, 0x0409, 4, "Meziantou Boxes Light"),
            (3, 1, 0x0409, 6, "MeziantouBoxes-Light"),
            (3, 1, 0x0409, 16, "Meziantou Boxes"),
            (3, 1, 0x040C, 16, "Boîtes Meziantou"),
            (3, 1, 0x0409, 17, "Light"));
        directory.Write("light.ttf", light.Build());
        directory.Write("regular.ttf", FontSamples.Boxes().Build());
        directory.Write("curves.ttf", FontSamples.Curves().Build());

        var fonts = SystemFontCollection.Create([directory.Path]);

        Assert.Equal(new[] { "Light", "Regular" }, fonts.GetFamily("Meziantou Boxes").Select(font => font.SubfamilyName).ToArray());
        Assert.Equal(new[] { "Light", "Regular" }, fonts.GetFamily("MEZIANTOU BOXES").Select(font => font.SubfamilyName).ToArray());
        Assert.Equal("Light", Assert.Single(fonts.GetFamily("boÎtes meziantou")).SubfamilyName);
        Assert.Equal("Light", Assert.Single(fonts.GetFamily("Meziantou Boxes Light")).SubfamilyName);
        Assert.Empty(fonts.GetFamily("Meziantou"));
        Assert.Equal("Meziantou Curves", Assert.Single(fonts.GetFamily("Meziantou Curves")).FamilyName);

        Assert.Equal("Light", fonts.FindByName("meziantou boxes light")?.SubfamilyName);
        Assert.Equal("Light", fonts.FindByName("MeziantouBoxes-Light")?.SubfamilyName);
        Assert.Equal("Regular", fonts.FindByName("Meziantou Boxes Regular")?.SubfamilyName);
        Assert.Null(fonts.FindByName("Meziantou Boxes"));
        Assert.Throws<ArgumentNullException>("familyName", () => fonts.GetFamily(null!));
        Assert.Throws<ArgumentNullException>("name", () => fonts.FindByName(null!));
    }

    [Fact]
    public void SystemFontsAreMatchedByWidthThenStyleThenWeight()
    {
        using var directory = new TemporaryFontDirectory();
        directory.Write("light.ttf", CreateStyledBoxes("Light", weight: 300).Build());
        directory.Write("medium.ttf", CreateStyledBoxes("Medium", weight: 500).Build());
        directory.Write("bold.ttf", CreateStyledBoxes("Bold", weight: 700).Build());
        directory.Write("italic.ttf", CreateStyledBoxes("Italic", weight: 400, italic: true).Build());
        directory.Write("oblique.ttf", SetObliqueBit(CreateStyledBoxes("Oblique", weight: 400).Build()));
        directory.Write("condensed.ttf", CreateStyledBoxes("Condensed", weight: 400, widthClass: 3).Build());

        var variable = CreateStyledBoxes("Variable", weight: 400, family: "Meziantou Variable");
        variable.Axes.Add(new FontAxisDefinition("wght", 100, 400, 600, "Weight"));
        directory.Write("variable.ttf", variable.Build());
        directory.Write("upright.ttf", CreateStyledBoxes("Upright", weight: 400, family: "Meziantou Upright").Build());
        directory.Write("slanted.ttf", SetObliqueBit(CreateStyledBoxes("Slanted", weight: 400, family: "Meziantou Slanted").Build()));

        var fonts = SystemFontCollection.Create([directory.Path]);

        Assert.Equal("Medium", fonts.Match("Meziantou Boxes")?.SubfamilyName);
        Assert.Equal("Medium", fonts.Match("Meziantou Boxes", 450)?.SubfamilyName);
        Assert.Equal("Bold", fonts.Match("Meziantou Boxes", 600)?.SubfamilyName);
        Assert.Equal("Bold", fonts.Match("Meziantou Boxes", 900)?.SubfamilyName);
        Assert.Equal("Light", fonts.Match("Meziantou Boxes", 300)?.SubfamilyName);
        Assert.Equal("Light", fonts.Match("Meziantou Boxes", 100)?.SubfamilyName);
        Assert.Equal("Bold", fonts.Match("Meziantou Boxes", 550)?.SubfamilyName);
        Assert.Equal("Italic", fonts.Match("meziantou boxes", 700, isItalic: true)?.SubfamilyName);
        Assert.Equal("Variable", fonts.Match("Meziantou Variable", 550)?.SubfamilyName);
        Assert.Equal("Variable", fonts.Match("Meziantou Variable", 1000, isItalic: true)?.SubfamilyName);
        Assert.Equal("Upright", fonts.Match("Meziantou Upright", isItalic: true)?.SubfamilyName);
        Assert.Equal("Slanted", fonts.Match("Meziantou Slanted")?.SubfamilyName);
        Assert.Null(fonts.Match("Meziantou Unknown"));

        directory.Write(Path.Combine("more", "slanted.ttf"), SetObliqueBit(CreateStyledBoxes("Oblique", weight: 400, family: "Meziantou Upright").Build()));
        var withOblique = SystemFontCollection.Create([directory.Path]);
        Assert.Equal("Oblique", withOblique.Match("Meziantou Upright", isItalic: true)?.SubfamilyName);
        Assert.Equal("Upright", withOblique.Match("Meziantou Upright")?.SubfamilyName);

        Assert.Throws<ArgumentNullException>("familyName", () => fonts.Match(null!));
        Assert.Throws<ArgumentOutOfRangeException>("weight", () => fonts.Match("Meziantou Boxes", 0));
        Assert.Throws<ArgumentOutOfRangeException>("weight", () => fonts.Match("Meziantou Boxes", 1001));

        static FontBuilder CreateStyledBoxes(string subfamily, int weight, bool italic = false, int widthClass = 5, string family = "Meziantou Boxes")
        {
            var builder = FontSamples.Boxes();
            builder.FamilyName = family + " " + subfamily;
            builder.TypographicFamilyName = family;
            builder.TypographicSubfamilyName = subfamily;
            builder.PostScriptName = null;
            builder.WeightClass = weight;
            builder.IsItalic = italic;
            builder.WidthClass = widthClass;
            return builder;
        }

        // Bit 9 of OS/2.fsSelection: the face is oblique.
        static byte[] SetObliqueBit(byte[] font)
        {
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
            for (var i = 0; i < tableCount; i++)
            {
                var record = font.AsSpan(12 + (i * 16), 16);
                if (record[..4].SequenceEqual("OS/2"u8))
                {
                    var selection = font.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]) + 62, 2);
                    BinaryPrimitives.WriteUInt16BigEndian(selection, (ushort)(BinaryPrimitives.ReadUInt16BigEndian(selection) | 0x0200));
                }
            }

            return font;
        }
    }

    [Fact]
    public void TheFontDirectoriesOfFontconfigAreReadFromItsConfiguration()
    {
        using var directory = new TemporaryFontDirectory();
        var configFile = directory.Write("fonts.conf", Encoding.UTF8.GetBytes("""
            <?xml version="1.0"?>
            <!DOCTYPE fontconfig SYSTEM "urn:fontconfig:fonts.dtd">
            <fontconfig>
              <dir>/usr/share/fonts</dir>
              <dir prefix="xdg">fonts</dir>
              <dir>~/.fonts</dir>
              <dir prefix="relative">local-fonts</dir>
              <dir>  </dir>
              <match target="pattern"><edit name="family"><string>sans-serif</string></edit></match>
              <include ignore_missing="yes">conf.d</include>
              <include ignore_missing="yes">missing.conf</include>
              <include ignore_missing="yes" prefix="xdg">fontconfig/fonts.conf</include>
            </fontconfig>
            """));
        directory.Write(Path.Combine("conf.d", "20-second.conf"), "<fontconfig><dir>/second</dir></fontconfig>"u8.ToArray());
        directory.Write(Path.Combine("conf.d", "10-first.conf"), "<fontconfig><dir>/first</dir><include>../fonts.conf</include></fontconfig>"u8.ToArray());
        directory.Write(Path.Combine("conf.d", "30-malformed.conf"), "<fontconfig><dir>/third</dir><dir>"u8.ToArray());
        directory.Write(Path.Combine("conf.d", "README"), "<fontconfig><dir>/readme</dir></fontconfig>"u8.ToArray());
        directory.Write(Path.Combine("conf.d", "not-numbered.conf"), "<fontconfig><dir>/not-numbered</dir></fontconfig>"u8.ToArray());
        directory.Write(Path.Combine("config", "fontconfig", "fonts.conf"), "<fontconfig><dir>/user</dir></fontconfig>"u8.ToArray());

        var variables = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FONTCONFIG_FILE"] = configFile,
            ["XDG_DATA_HOME"] = "/xdg/data",
            ["XDG_CONFIG_HOME"] = Path.Combine(directory.Path, "config"),
        };

        var directories = FontConfigFile.ReadDirectories(name => variables.GetValueOrDefault(name), "/home/user");

        Assert.Equal(
            new[]
            {
                Path.GetFullPath("/usr/share/fonts"),
                Path.GetFullPath("/xdg/data/fonts"),
                Path.GetFullPath("/home/user/.fonts"),
                Path.Combine(directory.Path, "local-fonts"),
                Path.GetFullPath("/first"),
                Path.GetFullPath("/second"),
                Path.GetFullPath("/third"),
                Path.GetFullPath("/user"),
            },
            directories.ToArray());

        // Without a home directory, the paths from it are ignored and the XDG directories have no default.
        variables.Remove("XDG_DATA_HOME");
        Assert.Equal(new[] { Path.GetFullPath("/usr/share/fonts"), Path.Combine(directory.Path, "local-fonts") }, FontConfigFile.ReadDirectories(name => variables.GetValueOrDefault(name), home: null).Take(2).ToArray());

        // The configuration is fonts.conf in $FONTCONFIG_PATH; reset-dirs removes the directories declared before it.
        variables.Remove("FONTCONFIG_FILE");
        var resetDirectory = Path.Combine(directory.Path, "reset");
        directory.Write(Path.Combine("reset", "fonts.conf"), "<fontconfig><dir>/before</dir><reset-dirs /><dir>/after</dir></fontconfig>"u8.ToArray());
        variables["FONTCONFIG_PATH"] = resetDirectory;
        Assert.Equal(new[] { Path.GetFullPath("/after") }, FontConfigFile.ReadDirectories(name => variables.GetValueOrDefault(name), "/home/user").ToArray());

        variables["FONTCONFIG_FILE"] = Path.Combine(directory.Path, "missing.conf");
        Assert.Empty(FontConfigFile.ReadDirectories(name => variables.GetValueOrDefault(name), "/home/user"));
    }

    [Fact]
    public void TheFontsInstalledOnTheMachineAreFound()
    {
        var directories = SystemFontDirectories.GetDefault(Environment.GetEnvironmentVariable);
        Assert.NotEmpty(directories);

        var fonts = SystemFontCollection.Default;
        Assert.Same(fonts, SystemFontCollection.Default);

        // The machines of the continuous integration may have no font on Linux.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
        {
            Assert.NotEmpty(fonts.Fonts);
        }

        foreach (var descriptor in fonts.Fonts.Take(10))
        {
            Assert.Equal(descriptor.FamilyName, FontCollection.Load(descriptor.FilePath)[descriptor.FaceIndex].FamilyName);
        }
    }

    [Fact]
    public void AnAfmFileGivesTheGlobalMetricsOfTheFont()
    {
        // Every global key of section 7.1, comments, keys the reader does not know, tabs, and the three line endings.
        var text = string.Join("\n",
            "StartFontMetrics 4.1",
            "Comment A comment\tkeeps its tabs",
            "FontName\tSample-Bold",
            "FullName Sample Bold  Condensed ",
            "FamilyName Sample",
            "Weight Bold",
            "Version 001.002",
            "Notice Copyright (c) Sample",
            "EncodingScheme AdobeStandardEncoding",
            "CharacterSet ExtendedRoman",
            "MappingScheme 3",
            "EscChar 255",
            "Characters 2",
            "IsBaseFont false",
            "IsCIDFont true",
            "FontBBox -166 -225.5 1000 931",
            "CapHeight 718",
            "XHeight 523.25",
            "Ascender 718",
            "Descender -207",
            "StdHW 76",
            "StdVW 88",
            "ItalicAngle -12.5",
            "IsFixedPitch false",
            "UnderlinePosition -100",
            "UnderlineThickness 50",
            "userKey 1 2 3",
            "FutureKey with values",
            "",
            "   \t ",
            "EndFontMetrics").Replace("Characters 2\n", "Characters 2\r\n", StringComparison.Ordinal).Replace("XHeight 523.25\n", "XHeight 523.25\r", StringComparison.Ordinal);

        var metrics = AfmFontMetrics.Parse(text);

        Assert.Equal("4.1", metrics.FormatVersion);
        Assert.Equal("Sample-Bold", metrics.FontName);
        Assert.Equal("Sample Bold  Condensed", metrics.FullName);
        Assert.Equal("Sample", metrics.FamilyName);
        Assert.Equal("Bold", metrics.Weight);
        Assert.Equal("001.002", metrics.Version);
        Assert.Equal("Copyright (c) Sample", metrics.Notice);
        Assert.Equal("AdobeStandardEncoding", metrics.EncodingScheme);
        Assert.Equal("ExtendedRoman", metrics.CharacterSet);
        Assert.Equal(["A comment\tkeeps its tabs"], metrics.Comments);
        Assert.Equal(3, metrics.MappingScheme);
        Assert.Equal(255, metrics.EscChar);
        Assert.Equal(2, metrics.CharacterCount);
        Assert.False(metrics.IsBaseFont);
        Assert.True(metrics.IsCIDFont);
        Assert.Equal(new AfmBox(-166, -225.5, 1000, 931), metrics.FontBBox);
        Assert.Equal(718, metrics.CapHeight);
        Assert.Equal(523.25, metrics.XHeight);
        Assert.Equal(718, metrics.Ascender);
        Assert.Equal(-207, metrics.Descender);
        Assert.Equal(76, metrics.StdHW);
        Assert.Equal(88, metrics.StdVW);
        Assert.Null(metrics.VVector);
        Assert.False(metrics.IsFixedV);

        // Outside StartDirection, the metrics are those of direction 0 (MetricsSets 0 is implied).
        Assert.Equal(0, metrics.MetricsSets);
        Assert.NotNull(metrics.Horizontal);
        Assert.Equal(-12.5, metrics.Horizontal.ItalicAngle);
        Assert.Equal(-100, metrics.Horizontal.UnderlinePosition);
        Assert.Equal(50, metrics.Horizontal.UnderlineThickness);
        Assert.Null(metrics.Horizontal.CharWidth);
        Assert.False(metrics.Horizontal.IsFixedPitch);
        Assert.Null(metrics.Vertical);
        Assert.Empty(metrics.CharacterMetrics);
        Assert.Empty(metrics.KerningPairs);
        Assert.Empty(metrics.TrackKerning);
        Assert.Empty(metrics.Composites);

        // A file without the optional keys.
        var minimal = AfmFontMetrics.Parse("StartFontMetrics 2.0\nEndFontMetrics");
        Assert.Equal("2.0", minimal.FormatVersion);
        Assert.Null(minimal.FontName);
        Assert.Null(minimal.FontBBox);
        Assert.True(minimal.IsBaseFont);
        Assert.Empty(minimal.Comments);
        Assert.NotNull(minimal.Horizontal);
        Assert.Null(minimal.Horizontal.ItalicAngle);
    }

    [Fact]
    public void AnAfmFileIsReadFromItsBytes()
    {
        // The notices of some files have characters of ISO-8859-1; a byte order mark is skipped, and what follows
        // EndFontMetrics is ignored (some files end with an end-of-file character).
        byte[] data = [0xEF, 0xBB, 0xBF, .. "StartFontMetrics 4.1\r\nNotice Caf"u8, 0xE9, .. "\r\nEndFontMetrics\r\n\u001A"u8];

        var metrics = AfmFontMetrics.Load(data);

        Assert.Equal("Café", metrics.Notice);
    }

    [Fact]
    public async Task AnAfmFileIsBoundedByTheLimits()
    {
        var data = "StartFontMetrics 4.1\nFontName Sample\nEndFontMetrics\n"u8.ToArray();
        var exact = new FontResourceLimits { MaxEncodedBytes = data.Length };
        var smaller = new FontResourceLimits { MaxEncodedBytes = data.Length - 1 };

        Assert.Equal("Sample", AfmFontMetrics.Load(data, exact).FontName);
        var exception = Assert.Throws<FontResourceLimitException>(() => AfmFontMetrics.Load(data, smaller));
        Assert.Equal(FontResourceLimitKind.EncodedBytes, exception.Kind);
        Assert.Equal(data.Length - 1, exception.Limit);
        Assert.Equal(data.Length, exception.Requested);

        using (var stream = new MemoryStream(data))
        {
            Assert.Equal("Sample", AfmFontMetrics.Load(stream, exact).FontName);
        }

        using (var stream = new MemoryStream(data))
        {
            Assert.Equal(FontResourceLimitKind.EncodedBytes, Assert.Throws<FontResourceLimitException>(() => AfmFontMetrics.Load(stream, smaller)).Kind);
        }

        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal("Sample", AfmFontMetrics.Load(stream, exact).FontName);
        }

        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal(FontResourceLimitKind.EncodedBytes, Assert.Throws<FontResourceLimitException>(() => AfmFontMetrics.Load(stream, smaller)).Kind);
        }

        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal("Sample", (await AfmFontMetrics.LoadAsync(stream, exact, TestContext.Current.CancellationToken)).FontName);
        }

        using (var stream = new NonSeekableStream(data))
        {
            Assert.Equal(FontResourceLimitKind.EncodedBytes, (await Assert.Throws<FontResourceLimitException>(async () => { await AfmFontMetrics.LoadAsync(stream, smaller, TestContext.Current.CancellationToken); })).Kind);
        }
    }

    [Fact]
    public void AnAfmFileGivesTheMetricsOfItsCharacters()
    {
        var metrics = AfmFontMetrics.Parse("""
            StartFontMetrics 4.1
            StartDirection 0
            CharWidth 600 0
            EndDirection
            StartCharMetrics 7
            C 102 ; WX 333 ; N f ; B 20 0 383 682 ; L i fi ; L l fl ;
            C 32;WX 250;N space;B 0 0 0 0;
            N fi ; C -1 ; W0X 556 ; B 31 0 521 683.5 ;
            CH <84AF> ; W 1000 -20 ; W1 0 -1000 ; VV 500 880 ; N kanji ;
            C 65 ; WY 10 ; W1X 5 ; W1Y -900 ; N A ;
            C 66 ; N f ; userKey value ;
            C 67 ; N fixed ;
            userLine without code
            EndCharMetrics
            EndFontMetrics
            """);

        Assert.HasCount(7, metrics.CharacterMetrics);
        var f = metrics.CharacterMetrics[0];
        Assert.Equal(102, f.Code);
        Assert.Equal("f", f.Name);
        Assert.Equal(new AfmVector(333, 0), f.Width);
        Assert.Null(f.VerticalWidth);
        Assert.Null(f.VVector);
        Assert.Equal(new AfmBox(20, 0, 383, 682), f.BoundingBox);
        Assert.Equal([new AfmLigature("i", "fi"), new AfmLigature("l", "fl")], f.Ligatures);

        var space = metrics.GetCharacter("space");
        Assert.NotNull(space);
        Assert.Equal(32, space.Code);
        Assert.Equal(new AfmVector(250, 0), space.Width);
        Assert.Equal(new AfmBox(0, 0, 0, 0), space.BoundingBox);
        Assert.Empty(space.Ligatures);

        // An unencoded character has the code -1, and is found by its name only.
        var ligature = metrics.GetCharacter("fi");
        Assert.NotNull(ligature);
        Assert.Equal(-1, ligature.Code);
        Assert.Equal(new AfmVector(556, 0), ligature.Width);
        Assert.Equal(new AfmBox(31, 0, 521, 683.5), ligature.BoundingBox);
        Assert.Null(metrics.GetCharacterByCode(-1));

        var kanji = metrics.GetCharacterByCode(0x84AF);
        Assert.NotNull(kanji);
        Assert.Equal("kanji", kanji.Name);
        Assert.Equal(new AfmVector(1000, -20), kanji.Width);
        Assert.Equal(new AfmVector(0, -1000), kanji.VerticalWidth);
        Assert.Equal(new AfmVector(500, 880), kanji.VVector);

        var a = metrics.GetCharacterByCode(65);
        Assert.NotNull(a);
        Assert.Equal(new AfmVector(0, 10), a.Width);
        Assert.Equal(new AfmVector(5, -900), a.VerticalWidth);
        Assert.Null(a.BoundingBox);

        // A name that two characters have is the one of the first character; both are in the list.
        Assert.Same(f, metrics.GetCharacter("f"));
        Assert.Equal(66, metrics.CharacterMetrics[5].Code);
        Assert.Same(metrics.CharacterMetrics[5], metrics.GetCharacterByCode(66));
        Assert.Null(metrics.GetCharacter("missing"));
        Assert.Null(metrics.GetCharacterByCode(1));

        // A character without width has the width of its direction.
        Assert.Null(metrics.CharacterMetrics[6].Width);
        Assert.Equal(new AfmVector(600, 0), metrics.GetWidth("fixed"));
        Assert.Equal(new AfmVector(333, 0), metrics.GetWidth(f));
        Assert.Null(metrics.GetWidth("missing"));
        Assert.Null(metrics.GetVerticalWidth("fixed"));
        Assert.Equal(new AfmVector(0, -1000), metrics.GetVerticalWidth(kanji));
        Assert.True(metrics.Horizontal?.IsFixedPitch);
    }

    [Fact]
    public void AnAfmFileDescribesItsWritingDirections()
    {
        var both = AfmFontMetrics.Parse("""
            StartFontMetrics 4.1
            MetricsSets 2
            VVector 500 880
            StartDirection 0
            ItalicAngle -12
            UnderlinePosition -100
            EndDirection
            StartDirection 1
            CharWidth 0 -1000
            IsFixedPitch false
            UnderlinePosition 400
            EndDirection
            EndFontMetrics
            """);

        Assert.Equal(2, both.MetricsSets);
        Assert.Equal(new AfmVector(500, 880), both.VVector);
        Assert.True(both.IsFixedV);
        Assert.NotNull(both.Horizontal);
        Assert.NotNull(both.Vertical);
        Assert.Equal(-12, both.Horizontal.ItalicAngle);
        Assert.Equal(-100, both.Horizontal.UnderlinePosition);
        Assert.False(both.Horizontal.IsFixedPitch);
        Assert.Null(both.Vertical.ItalicAngle);
        Assert.Equal(400, both.Vertical.UnderlinePosition);
        Assert.Equal(new AfmVector(0, -1000), both.Vertical.CharWidth);
        Assert.False(both.Vertical.IsFixedPitch);

        // StartDirection 2: both directions have the same metrics. IsFixedV can say that the vectors differ.
        var same = AfmFontMetrics.Parse("""
            StartFontMetrics 4.1
            MetricsSets 2
            VVector 500 880
            IsFixedV false
            StartDirection 2
            CharWidth 1000 0
            EndDirection
            EndFontMetrics
            """);
        Assert.False(same.IsFixedV);
        Assert.Equal(new AfmVector(1000, 0), same.Horizontal?.CharWidth);
        Assert.Equal(new AfmVector(1000, 0), same.Vertical?.CharWidth);
        Assert.True(same.Horizontal?.IsFixedPitch);
        Assert.True(same.Vertical?.IsFixedPitch);

        // The vertical font of section 11.3: direction 1 only.
        var vertical = AfmFontMetrics.Parse("""
            StartFontMetrics 3.0
            MetricsSets 1
            FontName Ryumin-Light-V
            StartDirection 1
            ItalicAngle 0
            CharWidth 0 -1000
            EndDirection
            StartCharMetrics 2
            CH <2121> ; B 0 0 0 0 ;
            CH <2122> ; B 211 -337 435 -81 ;
            EndCharMetrics
            EndFontMetrics
            """);
        Assert.Equal(1, vertical.MetricsSets);
        Assert.Null(vertical.Horizontal);
        Assert.Equal(new AfmVector(0, -1000), vertical.Vertical?.CharWidth);
        Assert.Equal(new AfmVector(0, -1000), vertical.GetVerticalWidth(vertical.GetCharacterByCode(0x2122)!));
        Assert.Null(vertical.GetWidth(vertical.GetCharacterByCode(0x2121)!));
    }

    [Fact]
    public void AnAfmFileGivesItsKerningAndItsCompositeCharacters()
    {
        var metrics = AfmFontMetrics.Parse("""
            StartFontMetrics 4.1
            StartCharMetrics 4
            C 65 ; WX 722 ; N A ;
            C 86 ; WX 722 ; N V ;
            C 121 ; WX 500 ; N y ;
            C 196 ; WX 333 ; N acute ;
            EndCharMetrics
            StartKernData
            StartTrackKern 3
            Comment Light kerning
            TrackKern -1 14 0 72 -1.89
            TrackKern -3 6 -.1 72 -3.78
            TrackKern -1 1 1 2 2
            EndTrackKern
            StartKernPairs 6
            KPX A V -129
            KPY A y 12
            KP V A -100 5
            KPH <41> <79> -92 0
            KPX A V -1
            KPX A missing -40
            EndKernPairs
            StartKernPairs1 1
            KPY A V -50
            EndKernPairs
            EndKernData
            StartComposites 2
            CC Aacute 2; PCC A 0 0; PCC acute 194 214;
            CC Vacute 2 ; PCC V 0 0 ; PCC acute 194.5 -3 ;
            EndComposites
            EndFontMetrics
            """);

        // A pair given by names gets the codes of its characters, a pair given by codes (KPH) gets their names.
        Assert.Equal(
            [
                new AfmKerningPair("A", "V", 65, 86, -129, 0),
                new AfmKerningPair("A", "y", 65, 121, 0, 12),
                new AfmKerningPair("V", "A", 86, 65, -100, 5),
                new AfmKerningPair("A", "y", 65, 121, -92, 0),
                new AfmKerningPair("A", "V", 65, 86, -1, 0),
                new AfmKerningPair("A", "missing", 65, null, -40, 0),
            ],
            metrics.KerningPairs);

        // When the file has a pair twice, the first one applies.
        Assert.Equal(new AfmVector(-129, 0), metrics.GetKerning("A", "V"));
        Assert.Equal(new AfmVector(-129, 0), metrics.GetKerning(65, 86));
        Assert.Equal(new AfmVector(0, 12), metrics.GetKerning("A", "y"));
        Assert.Equal(new AfmVector(-100, 5), metrics.GetKerning(86, 65));
        Assert.Equal(new AfmVector(-40, 0), metrics.GetKerning("A", "missing"));
        Assert.Equal(default, metrics.GetKerning("V", "y"));
        Assert.Equal(default, metrics.GetKerning(1, 2));

        Assert.Equal([new AfmKerningPair("A", "V", 65, 86, 0, -50)], metrics.VerticalKerningPairs);
        Assert.Equal(new AfmVector(0, -50), metrics.GetVerticalKerning("A", "V"));
        Assert.Equal(new AfmVector(0, -50), metrics.GetVerticalKerning(65, 86));
        Assert.Equal(default, metrics.GetVerticalKerning("V", "A"));

        // Section 9.1: constant below the minimum and above the maximum point size, linear in between.
        Assert.Equal([new AfmTrackKerning(-1, 14, 0, 72, -1.89), new AfmTrackKerning(-3, 6, -0.1, 72, -3.78), new AfmTrackKerning(-1, 1, 1, 2, 2)], metrics.TrackKerning);
        Assert.Equal(["Light kerning"], metrics.Comments);
        Assert.Equal(0, metrics.GetTrackKerning(-1, 6));
        Assert.Equal(0, metrics.GetTrackKerning(-1, 14));
        Assert.Equal(-0.945, metrics.GetTrackKerning(-1, 43), 1e-12);
        Assert.Equal(-1.89, metrics.GetTrackKerning(-1, 72));
        Assert.Equal(-1.89, metrics.GetTrackKerning(-1, 100));
        Assert.Equal(-0.1, metrics.GetTrackKerning(-3, 6));
        Assert.Equal(-1.94, metrics.GetTrackKerning(-3, 39), 1e-12);
        Assert.Equal(0, metrics.GetTrackKerning(-2, 12));
        Assert.Equal(1, new AfmTrackKerning(0, 10, 1, 10, 2).GetKerning(10));
        Assert.Equal(2, new AfmTrackKerning(0, 10, 1, 10, 2).GetKerning(11));

        // Point sizes so close that the slope of the line is not a number.
        Assert.Equal(0, new AfmTrackKerning(0, 0, -5, 1e-320, 5).GetKerning(5e-321), 1e-9);

        Assert.HasCount(2, metrics.Composites);
        Assert.Equal("Aacute", metrics.Composites[0].Name);
        Assert.Equal([new AfmCompositePart("A", 0, 0), new AfmCompositePart("acute", 194, 214)], metrics.Composites[0].Parts);
        Assert.Equal("Vacute", metrics.Composites[1].Name);
        Assert.Equal([new AfmCompositePart("V", 0, 0), new AfmCompositePart("acute", 194.5, -3)], metrics.Composites[1].Parts);
    }

    [Fact]
    public void KerningPairsOutsideKernDataAreRead()
    {
        // Section 9 puts the pairs and the tracks in StartKernData; some files have them directly in the file.
        var metrics = AfmFontMetrics.Parse("""
            StartFontMetrics 4.1
            StartKernPairs 1
            KPX A V -70
            EndKernPairs
            StartTrackKern 1
            TrackKern 0 6 0 72 -1
            EndTrackKern
            EndFontMetrics
            """);

        Assert.Equal([new AfmKerningPair("A", "V", null, null, -70, 0)], metrics.KerningPairs);
        Assert.Equal(new AfmVector(-70, 0), metrics.GetKerning("A", "V"));
        Assert.Equal(-1, metrics.GetTrackKerning(0, 72));
    }

    [Theory]
    [InlineData("", 1, "the file is empty.")]
    [InlineData("FontName Sample\nEndFontMetrics", 1, "the file does not start with StartFontMetrics.")]
    [InlineData("\nStartFontMetrics 4.1\nEndFontMetrics", 1, "the file does not start with StartFontMetrics.")]
    [InlineData("StartFontMetrics\nEndFontMetrics", 1, "StartFontMetrics has no version.")]
    [InlineData("StartFontMetrics 4.1\nFontName Sample", 2, "EndFontMetrics is missing.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nC 32 ; WX 250 ;\nEndFontMetrics", 4, "EndFontMetrics is not expected in the StartCharMetrics section.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nC 32 ; WX 250 ;", 3, "the StartCharMetrics section is not closed.")]
    [InlineData("StartFontMetrics 4.1\nEndCharMetrics\nEndFontMetrics", 2, "EndCharMetrics is not expected in the StartFontMetrics section.")]
    [InlineData("StartFontMetrics 4.1\nEndKernPairs\nEndFontMetrics", 2, "EndKernPairs is not expected in the StartFontMetrics section.")]
    [InlineData("StartFontMetrics 4.1\nStartKernData\nStartCharMetrics 1\nEndCharMetrics\nEndKernData\nEndFontMetrics", 3, "StartCharMetrics is not expected in the StartKernData section.")]
    [InlineData("StartFontMetrics 4.1\nStartDirection 0\nStartDirection 1\nEndDirection\nEndFontMetrics", 3, "StartDirection is not expected in the StartDirection section.")]
    [InlineData("StartFontMetrics 4.1\nCapHeight abc\nEndFontMetrics", 2, "the value 'abc' of CapHeight is not a number in range.")]
    [InlineData("StartFontMetrics 4.1\nCapHeight NaN\nEndFontMetrics", 2, "the value 'NaN' of CapHeight is not a number in range.")]
    [InlineData("StartFontMetrics 4.1\nCapHeight Infinity\nEndFontMetrics", 2, "the value 'Infinity' of CapHeight is not a number in range.")]
    [InlineData("StartFontMetrics 4.1\nCapHeight 1e30\nEndFontMetrics", 2, "the value '1e30' of CapHeight is not a number in range.")]
    [InlineData("StartFontMetrics 4.1\nCapHeight 0x10\nEndFontMetrics", 2, "the value '0x10' of CapHeight is not a number in range.")]
    [InlineData("StartFontMetrics 4.1\nFontBBox 1 2 3\nEndFontMetrics", 2, "FontBBox has fewer values than it needs.")]
    [InlineData("StartFontMetrics 4.1\nCharacters 1.5\nEndFontMetrics", 2, "the value '1.5' of Characters is not an integer.")]
    [InlineData("StartFontMetrics 4.1\nIsFixedPitch yes\nEndFontMetrics", 2, "the value 'yes' of IsFixedPitch is not true or false.")]
    [InlineData("StartFontMetrics 4.1\nMetricsSets 3\nEndFontMetrics", 2, "MetricsSets is 3, which is not 0, 1 or 2.")]
    [InlineData("StartFontMetrics 4.1\nStartDirection 3\nEndDirection\nEndFontMetrics", 2, "StartDirection has the direction 3, which is not 0, 1 or 2.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nWX 250 ; N space ;\nEndCharMetrics\nEndFontMetrics", 3, "the metrics of a character have no code (C or CH).")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nCH 41 ;\nEndCharMetrics\nEndFontMetrics", 3, "the value '41' of CH is not a hexadecimal code between angle brackets.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nCH <FFFFFFFF> ;\nEndCharMetrics\nEndFontMetrics", 3, "the value '<FFFFFFFF>' of CH is not a hexadecimal code between angle brackets.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nCH <> ;\nEndCharMetrics\nEndFontMetrics", 3, "the value '<>' of CH is not a hexadecimal code between angle brackets.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nC 65 ; L fi ;\nEndCharMetrics\nEndFontMetrics", 3, "L has fewer values than it needs.")]
    [InlineData("StartFontMetrics 4.1\nStartKernPairs 1\nKPX A V\nEndKernPairs\nEndFontMetrics", 3, "KPX has fewer values than it needs.")]
    [InlineData("StartFontMetrics 4.1\nStartTrackKern 1\nTrackKern -1 14 0 72\nEndTrackKern\nEndFontMetrics", 3, "TrackKern has fewer values than it needs.")]
    [InlineData("StartFontMetrics 4.1\nStartComposites 1\nPCC A 0 0 ; CC Aacute 1 ;\nEndComposites\nEndFontMetrics", 3, "PCC comes before the CC key of its composite character.")]
    [InlineData("StartFontMetrics 4.1\nStartCharMetrics 1\nC 65 ; N 01234567890123456789012345678901234567890123456789 ; WX wide0123456789012345678901234567890123456789 ;\nEndCharMetrics\nEndFontMetrics", 3, "the value 'wide0123456789012345678901234567...' of WX is not a number in range.")]
    public void InvalidAfmFilesAreRejectedWithTheLineOfTheError(string text, int line, string message)
    {
        var exception = Assert.Throws<InvalidFontException>(() => AfmFontMetrics.Parse(text));

        Assert.Equal($"The AFM file is invalid: line {line}: {message}", exception.Message);
    }

    [Theory]
    [InlineData("StartMasterFontMetrics 4.0\nFontName MyriadMM\nEndMasterFontMetrics", "AMFM")]
    [InlineData("StartCompFontMetrics 3.0\nFontName Ryumin-Light-V\nEndCompFontMetrics", "ACFM")]
    public void MultipleMasterAndCompositeMetricsFilesAreNotSupported(string text, string feature)
    {
        var exception = Assert.Throws<UnsupportedFontFeatureException>(() => AfmFontMetrics.Parse(text));

        Assert.Equal(feature, exception.Feature);
    }

    [Fact]
    public void TheFontLoaderSaysThatAnAfmFileHasNoGlyphs()
    {
        var environment = new PooledFontEnvironment(FontResourceLimits.Default, 1024 * 1024);

        var exception = Assert.Throws<UnknownFontFormatException>(() => FontLoader.Load("StartFontMetrics 4.1\nEndFontMetrics\n"u8, environment));

        Assert.Equal("The data is not a font: it is an AFM file (Adobe Font Metrics), which has metrics but no glyphs. Read it with AfmFontMetrics.", exception.Message);
        Assert.Contains("AfmFontMetrics", Assert.Throws<UnknownFontFormatException>(() => FontLoader.Load("StartMasterFontMetrics 4.0\n"u8, environment)).Message);
    }

    [Fact]
    public void TheAfmFilesOfTheStandardFontsAreRead()
    {
        var directory = FixtureRoot.GetDirectory() / "font-suites" / "adobe-core14-afm";
        var files = Directory.EnumerateFiles(directory, "*.afm").Order(StringComparer.Ordinal).ToList();
        Assert.HasCount(14, files);

        var fonts = new Dictionary<string, AfmFontMetrics>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var text = File.ReadAllText(file, Encoding.Latin1);
            AfmFontMetrics metrics;
            using (var stream = File.OpenRead(file))
            {
                metrics = AfmFontMetrics.Load(stream);
            }

            fonts.Add(Path.GetFileNameWithoutExtension(file), metrics);
            Assert.Equal("4.1", metrics.FormatVersion);
            Assert.Equal(Path.GetFileNameWithoutExtension(file), metrics.FontName);
            Assert.NotNull(metrics.FontBBox);
            Assert.NotNull(metrics.Horizontal);
            Assert.Null(metrics.Vertical);

            // The counts the file announces are the entries it has.
            Assert.HasCount(GetAnnouncedCount(text, "StartCharMetrics"), metrics.CharacterMetrics);
            Assert.HasCount(GetAnnouncedCount(text, "StartKernPairs"), metrics.KerningPairs);
            Assert.All(metrics.CharacterMetrics, character =>
            {
                Assert.NotNull(character.Name);
                Assert.NotNull(character.Width);
                Assert.NotNull(character.BoundingBox);
                Assert.Same(character, metrics.GetCharacter(character.Name));
            });
            Assert.All(metrics.KerningPairs, pair => Assert.True(pair.First is not null && pair.Second is not null && metrics.GetCharacter(pair.First) is not null && metrics.GetCharacter(pair.Second) is not null));
        }

        var helvetica = fonts["Helvetica"];
        Assert.Equal("Helvetica", helvetica.FamilyName);
        Assert.Equal("Medium", helvetica.Weight);
        Assert.Equal("002.000", helvetica.Version);
        Assert.Equal("AdobeStandardEncoding", helvetica.EncodingScheme);
        Assert.Equal("ExtendedRoman", helvetica.CharacterSet);
        Assert.Equal(new AfmBox(-166, -225, 1000, 931), helvetica.FontBBox);
        Assert.Equal(718, helvetica.CapHeight);
        Assert.Equal(523, helvetica.XHeight);
        Assert.Equal(-207, helvetica.Descender);
        Assert.Equal(88, helvetica.StdVW);
        Assert.Equal(-100, helvetica.Horizontal?.UnderlinePosition);
        Assert.Equal(50, helvetica.Horizontal?.UnderlineThickness);
        Assert.Equal(0, helvetica.Horizontal?.ItalicAngle);
        Assert.False(helvetica.Horizontal?.IsFixedPitch);
        Assert.Contains("Helvetica is a trademark of Linotype-Hell AG", helvetica.Notice);
        Assert.Equal(new AfmVector(667, 0), helvetica.GetWidth("A"));
        Assert.Equal(new AfmVector(278, 0), helvetica.GetCharacterByCode(32)?.Width);
        Assert.Equal([new AfmLigature("i", "fi"), new AfmLigature("l", "fl")], helvetica.GetCharacter("f")?.Ligatures);
        Assert.Equal(new AfmBox(13, 0, 266, 901), helvetica.GetCharacter("Idieresis")?.BoundingBox);
        Assert.Equal(-1, helvetica.GetCharacter("Idieresis")?.Code);
        Assert.Equal(new AfmVector(-70, 0), helvetica.GetKerning("A", "V"));
        Assert.Equal(new AfmVector(-120, 0), helvetica.GetKerning("T", "o"));
        Assert.Equal(new AfmVector(-70, 0), helvetica.GetKerning(65, 86));

        Assert.True(fonts["Courier"].Horizontal?.IsFixedPitch);
        Assert.Equal(new AfmBox(-23, -250, 715, 805), fonts["Courier"].FontBBox);
        Assert.All(fonts["Courier"].CharacterMetrics, character => Assert.Equal(new AfmVector(600, 0), character.Width));
        Assert.Equal(new AfmBox(-168, -218, 1000, 898), fonts["Times-Roman"].FontBBox);
        Assert.Equal(450, fonts["Times-Roman"].XHeight);
        Assert.Equal("FontSpecific", fonts["Symbol"].EncodingScheme);
        Assert.Equal("Special", fonts["Symbol"].CharacterSet);
        Assert.Equal("alpha", fonts["Symbol"].GetCharacterByCode(97)?.Name);
        Assert.Equal(new AfmVector(631, 0), fonts["Symbol"].GetWidth("alpha"));
        Assert.HasCount(202, fonts["ZapfDingbats"].CharacterMetrics);

        static int GetAnnouncedCount(string text, string key)
        {
            var line = text.Split("\r\n").SingleOrDefault(line => line.StartsWith(key + " ", StringComparison.Ordinal));
            return line is null ? 0 : int.Parse(line.AsSpan(key.Length + 1), CultureInfo.InvariantCulture);
        }
    }

    [Fact]
    public void AfmFontMetricsMeasureText()
    {
        #region AfmFontMetrics_Load
        // An AFM file gives the metrics of a PostScript font without its glyphs, here two characters of Helvetica.
        var metrics = AfmFontMetrics.Parse("""
            StartFontMetrics 4.1
            FontName Helvetica
            StartCharMetrics 2
            C 65 ; WX 667 ; N A ; B 14 0 654 718 ;
            C 86 ; WX 667 ; N V ; B 20 0 647 718 ;
            EndCharMetrics
            StartKernData
            StartKernPairs 1
            KPX A V -70
            EndKernPairs
            EndKernData
            EndFontMetrics
            """);

        // The metrics are in 1/1000 of the point size: the width of "AV" at 12 points, with its kerning.
        var units = metrics.GetWidth("A")!.Value.X + metrics.GetKerning("A", "V").X + metrics.GetWidth("V")!.Value.X;
        var width = units * 12 / 1000;
        Assert.Equal(15.168, width);
        #endregion
    }

    private static byte[] WriteNameTable(params (int Platform, int Encoding, int Language, int Id, string Value)[] records)
    {
        var header = new List<byte>();
        var storage = new List<byte>();
        void UInt16(List<byte> target, int value)
        {
            target.Add((byte)(value >> 8));
            target.Add((byte)value);
        }

        UInt16(header, 0);
        UInt16(header, records.Length);
        UInt16(header, 6 + (records.Length * 12));
        foreach (var (platform, encoding, language, id, value) in records)
        {
            var data = Encoding.BigEndianUnicode.GetBytes(value);
            UInt16(header, platform);
            UInt16(header, encoding);
            UInt16(header, language);
            UInt16(header, id);
            UInt16(header, data.Length);
            UInt16(header, storage.Count);
            storage.AddRange(data);
        }

        return [.. header, .. storage];
    }

    /// <summary>Moves the strings of the records of the <c>name</c> table of a font that have one of the values outside the table.</summary>
    private static byte[] MoveNamesOutsideTheTable(byte[] font, params string[] values)
    {
        var result = (byte[])font.Clone();
        var tableCount = BinaryPrimitives.ReadUInt16BigEndian(result.AsSpan(4));
        for (var i = 0; i < tableCount; i++)
        {
            var directoryRecord = result.AsSpan(12 + (i * 16), 16);
            if (!directoryRecord[..4].SequenceEqual("name"u8))
                continue;

            var table = result.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(directoryRecord[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(directoryRecord[12..]));
            var storage = BinaryPrimitives.ReadUInt16BigEndian(table[4..]);
            var moved = 0;
            for (var j = 0; j < BinaryPrimitives.ReadUInt16BigEndian(table[2..]); j++)
            {
                var record = table.Slice(6 + (j * 12), 12);
                var data = table.Slice(storage + BinaryPrimitives.ReadUInt16BigEndian(record[10..]), BinaryPrimitives.ReadUInt16BigEndian(record[8..]));
                var value = BinaryPrimitives.ReadUInt16BigEndian(record) == 1 ? Encoding.Latin1.GetString(data) : Encoding.BigEndianUnicode.GetString(data);
                if (values.Contains(value, StringComparer.Ordinal))
                {
                    BinaryPrimitives.WriteUInt16BigEndian(record[10..], (ushort)(table.Length - storage));
                    moved++;
                }
            }

            Assert.Equal(values.Length, moved);
            return result;
        }

        throw new ArgumentException("The font has no name table", nameof(font));
    }

    private sealed class TemporaryFontDirectory : IDisposable
    {
        public TemporaryFontDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "font-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string relativePath, byte[] data)
        {
            var path = System.IO.Path.Combine(Path, relativePath);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        // Short reads, as a network stream does.
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, Math.Min(count, 100));

        public override int Read(Span<byte> buffer) => _inner.Read(buffer[..Math.Min(buffer.Length, 100)]);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer[..Math.Min(buffer.Length, 100)], cancellationToken);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
