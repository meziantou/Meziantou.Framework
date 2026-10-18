using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The managed OpenType / WOFF / WOFF2 reader. The expected values were read with fontTools and HarfBuzz from the same
/// files (the bundled fonts, the fonts of the web-platform-tests and the files of the Resources folder). The files of the
/// Resources folder are the bundled Ahem font converted by fontTools 4.60.1: <c>Ahem.woff</c> (<c>TTFont.flavor = "woff"</c>)
/// and <c>Ahem-transformed-hmtx.woff2</c> (<c>woff2.compress(..., transform_tables={"glyf", "loca", "hmtx"})</c>).
/// </summary>
public sealed class FontReaderTests
{
    private static string Resource(string name) => Path.Combine(TestPaths.Root, "tests", "Meziantou.Framework.WebPlatform.Tests", "Css", "Fonts", "Resources", name);

    private static string Bundled(string name) => Path.Combine(TestPaths.Root, "src", "Meziantou.Framework.WebPlatform", "Css", "Fonts", "Bundled", name);

    private static OpenTypeFont LoadFace(string path, int index = 0) => (FontFile.Load(File.ReadAllBytes(path)) ?? throw new InvalidOperationException(path + " is invalid")).Faces[index];

    private static uint Tag(string value) => OpenTypeTag.TryParse(value, out var tag) ? tag : throw new ArgumentException(value, nameof(value));

    [Fact]
    public void Ahem_Metrics()
    {
        var font = BundledFonts.Ahem.Font;
        Assert.Equal(FontFileFormat.OpenType, font.File.Format);
        Assert.Equal("Ahem", font.FamilyName);
        Assert.Equal("Regular", font.SubfamilyName);
        Assert.Equal("Ahem", font.PostScriptName);
        Assert.Equal(1000, font.UnitsPerEm);
        Assert.Equal(278, font.GlyphCount);
        Assert.Equal(400, font.WeightClass);
        Assert.False(font.IsItalic);
        Assert.False(font.Variations.IsVariable);

        var metrics = font.Metrics;
        Assert.Equal(800, metrics.Ascent);
        Assert.Equal(200, metrics.Descent);
        Assert.Equal(0, metrics.LineGap);
        Assert.Equal(800, metrics.XHeight);
        Assert.Equal(800, metrics.CapHeight);
        Assert.Equal(-133, metrics.UnderlinePosition);
        Assert.Equal(20, metrics.UnderlineThickness);
        Assert.Equal(259, metrics.StrikeoutPosition);
        Assert.Equal(50, metrics.StrikeoutThickness);
    }

    [Fact]
    public void Ahem_CharacterMapAndAdvances()
    {
        var font = BundledFonts.Ahem.Font;
        Assert.Equal(58, font.GetGlyphId('X'));
        Assert.Equal(82, font.GetGlyphId('p'));
        Assert.Equal(100, font.GetGlyphId(0xC9));
        Assert.Equal(0, font.GetGlyphId(0x4E01));
        Assert.Equal(0, font.GetGlyphId(0x1F600));
        Assert.False(font.HasGlyph(0x4E01));

        // https://web-platform-tests.org/writing-tests/ahem.html: 1em glyphs, and the spaces of the description of the font
        Assert.Equal(1000, font.GetAdvanceWidth(font.GetGlyphId('X')));
        Assert.Equal(1000, font.GetAdvanceWidth(font.GetGlyphId(' ')));
        Assert.Equal(500, font.GetAdvanceWidth(font.GetGlyphId(0x2002)));
        Assert.Equal(0, font.GetAdvanceWidth(font.GetGlyphId(0x200B)));
        Assert.Equal(100, font.GetAdvanceWidth(font.GetGlyphId(0x200A)));
        Assert.Equal(1000, font.GetAdvanceWidth(font.GetGlyphId(0x6C34)));

        // The glyph boxes: X fills the em square, p is below the baseline and É above it
        Assert.True(font.TryGetGlyphBounds(font.GetGlyphId('X'), out var x));
        Assert.Equal(new GlyphBounds(0, -200, 1000, 800), x);
        Assert.True(font.TryGetGlyphBounds(font.GetGlyphId('p'), out var p));
        Assert.Equal(new GlyphBounds(0, -200, 1000, 0), p);
        Assert.True(font.TryGetGlyphBounds(font.GetGlyphId(0xC9), out var e));
        Assert.Equal(new GlyphBounds(0, 0, 1000, 800), e);
        Assert.False(font.TryGetGlyphBounds(font.GetGlyphId(' '), out _));

        // No vertical metrics: the advance height is the ascent and the descent
        Assert.False(font.HasVerticalMetrics);
        Assert.Equal(1000, font.GetAdvanceHeight(font.GetGlyphId('X')));
        Assert.Equal(0, font.GetKerning(font.GetGlyphId('X'), font.GetGlyphId('X')));
    }

    [Theory]
    [InlineData("Ahem.woff")]
    [InlineData("Ahem-transformed-hmtx.woff2")]
    public void Ahem_WoffAndWoff2_DecodeToTheSameFont(string file)
    {
        var expected = BundledFonts.Ahem.Font;
        var font = LoadFace(Resource(file));
        Assert.Equal(file.EndsWith(".woff2", StringComparison.Ordinal) ? FontFileFormat.Woff2 : FontFileFormat.Woff, font.File.Format);
        Assert.False(font.File.IsCollection);
        Assert.Equal(expected.Metrics, font.Metrics);
        Assert.Equal(expected.GlyphCount, font.GlyphCount);
        for (var glyph = (ushort)0; glyph < expected.GlyphCount; glyph++)
        {
            Assert.Equal(expected.GetAdvanceWidth(glyph), font.GetAdvanceWidth(glyph));
            Assert.Equal(expected.GetLeftSideBearing(glyph), font.GetLeftSideBearing(glyph));
            Assert.Equal(expected.TryGetGlyphBounds(glyph, out var expectedBounds), font.TryGetGlyphBounds(glyph, out var bounds));
            Assert.Equal(expectedBounds, bounds);
        }

        for (var c = 0; c < 0x10000; c++)
        {
            Assert.Equal(expected.GetGlyphId(c), font.GetGlyphId(c));
        }

        // The decoded data is an sfnt font whose tables are the ones of the original font (in the order of their tags)
        var reloaded = FontFile.Load(font.Data);
        Assert.NotNull(reloaded);
        Assert.Equal(FontFileFormat.OpenType, reloaded.Format);
        Assert.Equal(expected.TableTags.Order(), font.TableTags);
        Assert.Equal(expected.GetTable(OpenTypeTag.Cmap).ToArray(), font.GetTable(OpenTypeTag.Cmap).ToArray());
        Assert.Equal(expected.GetTable(OpenTypeTag.Hmtx).ToArray(), font.GetTable(OpenTypeTag.Hmtx).ToArray());
    }

    [Fact]
    public void Woff2_ExtraneousDataIsRejected()
    {
        // https://www.w3.org/TR/WOFF2/#conform-extraneous-reject: the file ends with its last block (the compressed data here,
        // padded to 4 bytes at most)
        var data = File.ReadAllBytes(Resource("Ahem-transformed-hmtx.woff2"));
        Assert.NotNull(FontFile.Load(data));

        var extended = new byte[((data.Length + 3) & ~3) + 4];
        data.CopyTo(extended, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(extended.AsSpan(8), (uint)extended.Length);
        Assert.Null(FontFile.Load(extended));

        // The reserved field of the header is ignored
        var reserved = (byte[])data.Clone();
        reserved[15] = 1;
        Assert.NotNull(FontFile.Load(reserved));
    }

    [Fact]
    public void BundledFonts_AllLoad()
    {
        Assert.Equal(12, BundledFonts.All.Count);
        foreach (var bundled in BundledFonts.All)
        {
            var font = bundled.Font;
            Assert.Equal(bundled.Family, font.FamilyName);
            Assert.True(font.HasGlyph('a'), bundled.FileName);
            if (bundled != BundledFonts.Ahem)
            {
                Assert.True(font.HasGlyph(0x03B1), bundled.FileName); // Greek alpha
                Assert.True(font.HasGlyph(0x0416), bundled.FileName); // Cyrillic zhe
            }

            Assert.NotEmpty(font.GetName(OpenTypeFont.NameId.Copyright) ?? "");
        }

        Assert.Equal(["Arimo-VF.woff2", "Arimo-Italic-VF.woff2"], BundledFonts.GetFamily("arimo").Select(font => font.FileName));
        Assert.Equal("Tinos", BundledFonts.GetGenericFamily("serif"));
        Assert.Equal("Arimo", BundledFonts.GetGenericFamily("sans-serif"));
        Assert.Equal("Cousine", BundledFonts.GetGenericFamily("monospace"));
        Assert.Equal("STIX Two Math", BundledFonts.GetGenericFamily("math"));
        Assert.NotNull(BundledFonts.GetFamilyFonts("STIX Two Math")[0].Font.MathTable);
        Assert.Null(BundledFonts.GetGenericFamily("fantasy"));
    }

    [Fact]
    public void Tinos_MetricsAndKerning()
    {
        var font = LoadFace(Bundled("Tinos-Regular.woff2"));
        Assert.Equal(FontFileFormat.Woff2, font.File.Format);
        Assert.Equal("Tinos-Regular", font.PostScriptName);
        Assert.Equal(2048, font.UnitsPerEm);

        // USE_TYPO_METRICS is not set: the metrics of the hhea table
        Assert.False(font.UseTypoMetrics);
        var metrics = font.Metrics;
        Assert.Equal(1825, metrics.Ascent);
        Assert.Equal(443, metrics.Descent);
        Assert.Equal(87, metrics.LineGap);
        Assert.Equal(940, metrics.XHeight);
        Assert.Equal(1341, metrics.CapHeight);

        var a = font.GetGlyphId('A');
        var v = font.GetGlyphId('V');
        Assert.Equal(1479, font.GetAdvanceWidth(a));
        Assert.Equal(20, font.GetLeftSideBearing(a));
        Assert.Equal(512, font.GetAdvanceWidth(font.GetGlyphId(' ')));

        // The pair adjustments of the kern feature of GPOS
        Assert.Equal(-264, font.GetKerning(a, v));
        Assert.Equal(-143, font.GetKerning(font.GetGlyphId('T'), font.GetGlyphId('o')));
        Assert.Equal(0, font.GetKerning(v, v));
        Assert.True(font.GetPairAdjustment(a, v, Tag("liga")).IsEmpty);
    }

    [Fact]
    public void Tinos_BoldItalic_Style()
    {
        var font = LoadFace(Bundled("Tinos-BoldItalic.woff2"));
        Assert.Equal("Bold Italic", font.SubfamilyName);
        Assert.Equal(700, font.WeightClass);
        Assert.True(font.IsBold);
        Assert.True(font.IsItalic);
        Assert.False(font.IsOblique);
        Assert.Equal(-16.33, font.ItalicAngle, 2);
    }

    [Fact]
    public void Cousine_IsMonospaced()
    {
        var font = LoadFace(Bundled("Cousine-Regular.woff2"));
        Assert.True(font.IsFixedPitch);
        Assert.True(font.UseTypoMetrics);
        Assert.Equal(1705, font.Metrics.Ascent);
        Assert.Equal(615, font.Metrics.Descent);
        foreach (var c in "AVXpx0 ")
        {
            Assert.Equal(1229, font.GetAdvanceWidth(font.GetGlyphId(c)));
        }

        Assert.Equal(0, font.GetKerning(font.GetGlyphId('A'), font.GetGlyphId('V')));
    }

    [Fact]
    public void Arimo_VariableFont()
    {
        var font = LoadFace(Bundled("Arimo-VF.woff2"));
        var axis = Assert.Single(font.Variations.Axes);
        Assert.Equal(new FontVariationAxis(OpenTypeTag.WeightAxis, 400, 400, 700, IsHidden: false, axis.NameId), axis);
        Assert.Equal(4, font.Variations.NamedInstances.Count);
        Assert.True(font.UseTypoMetrics);
        Assert.Equal(1854, font.Metrics.Ascent);
        Assert.Equal(434, font.Metrics.Descent);
        Assert.Equal(67, font.Metrics.LineGap);

        // The advances of the instances, with the deltas of HVAR (the values clamped to the axis)
        var a = font.GetGlyphId('A');
        var v = font.GetGlyphId('V');
        var regular = font.Variations.CreateInstance([]);
        var bold = font.Variations.CreateInstance([new FontVariationSetting(OpenTypeTag.WeightAxis, 700)]);
        var semiBold = font.Variations.CreateInstance([new FontVariationSetting(OpenTypeTag.WeightAxis, 550)]);
        var black = font.Variations.CreateInstance([new FontVariationSetting(OpenTypeTag.WeightAxis, 900)]);
        Assert.True(regular.IsDefault);
        Assert.Equal([1.0], bold.NormalizedCoordinates);
        Assert.Equal([0.5], semiBold.NormalizedCoordinates);
        Assert.Equal([1.0], black.NormalizedCoordinates);
        Assert.Equal(1366, font.GetAdvanceWidth(a, regular));
        Assert.Equal(1479, font.GetAdvanceWidth(a, bold));
        // An advance is a whole number of font units in an instance too: the delta is rounded, as in HarfBuzz and FreeType
        Assert.Equal(1423, font.GetAdvanceWidth(a, semiBold));
        Assert.Equal(1366, font.GetAdvanceWidth(v, bold));

        // The kerning of the instances (the variation indexes of the GPOS value records)
        Assert.Equal(-152, font.GetKerning(a, v));
        Assert.Equal(-152, font.GetKerning(a, v, bold));
        Assert.Equal(-227, font.GetKerning(font.GetGlyphId('T'), font.GetGlyphId('o')));
    }

    [Fact]
    public void KernTable()
    {
        // GenR102.woff2 has a kern table and no GPOS table
        var font = LoadFace(TestPaths.External("wpt", "css", "css-font-loading", "resources", "GenR102.woff2"));
        Assert.False(font.HasTable(OpenTypeTag.Gpos));
        Assert.Equal(-160, font.GetKerning(33, 54));
        Assert.Equal(-160, font.Face.GetLegacyKerning(33, 54));
        Assert.Equal(-200, font.GetKerning(54, 33));
        Assert.Equal(0, font.GetKerning(33, 33));
    }

    [Fact]
    public void CffFont_UsesTheKernFeatureOfGpos()
    {
        // Exo has a kern table and a GPOS table: the GPOS table wins
        var font = LoadFace(TestPaths.External("wpt", "css", "css-fonts", "support", "fonts", "Exo-DemiBold.otf"));
        Assert.True(font.HasTable(OpenTypeTag.Cff));
        Assert.Equal(-120, font.GetKerning(35, 56));
        Assert.Equal(-160, font.GetKerning(54, 81));

        // The bounding boxes of the CFF glyphs are computed from their charstrings
        Assert.True(font.TryGetGlyphBounds(35, out var bounds));
        Assert.True(bounds.XMax > bounds.XMin && bounds.YMax > bounds.YMin);
    }

    [Fact]
    public void Collection()
    {
        var file = FontFile.Load(File.ReadAllBytes(TestPaths.External("wpt", "css", "css-fonts", "variations", "resources", "ahem.ttc")));
        Assert.NotNull(file);
        Assert.Equal(FontFileFormat.Collection, file.Format);
        Assert.True(file.IsCollection);
        Assert.True(file.Faces.Count > 1);
        for (var i = 0; i < file.Faces.Count; i++)
        {
            Assert.Equal(i, file.Faces[i].FaceIndex);
            Assert.Same(file, file.Faces[i].File);
            Assert.Equal(1000, file.Faces[i].GetAdvanceWidth(file.Faces[i].GetGlyphId('X')));
        }
    }

    [Fact]
    public void MetricsVariations()
    {
        // UnderlineTest-VF.ttf varies the underline offset (UNDO axis) and size (UNDS axis) with MVAR
        var font = LoadFace(TestPaths.External("wpt", "css", "css-text-decor", "resources", "UnderlineTest-VF.ttf"));
        Assert.Equal(-150, font.Metrics.UnderlinePosition);
        Assert.Equal(100, font.Metrics.UnderlineThickness);

        var offset = font.Variations.CreateInstance([new FontVariationSetting(Tag("UNDO"), 1000)]);
        Assert.Equal(-300, font.GetMetrics(offset).UnderlinePosition);
        Assert.Equal(100, font.GetMetrics(offset).UnderlineThickness);

        var size = font.Variations.CreateInstance([new FontVariationSetting(Tag("UNDS"), 1000)]);
        Assert.Equal(-150, font.GetMetrics(size).UnderlinePosition);
        Assert.Equal(200, font.GetMetrics(size).UnderlineThickness);
    }

    [Fact]
    public void AxisVariations()
    {
        // variabletest_matching.ttf: wdth (50-100-200) and slnt (-90-0-90) have avar segment maps, ital an empty map
        var font = LoadFace(TestPaths.External("wpt", "css", "css-font-loading", "resources", "variabletest_matching.ttf"));
        Assert.Equal(["wdth", "slnt", "ital", "wght"], font.Variations.Axes.Select(axis => OpenTypeTag.ToString(axis.Tag)));

        static double[] Normalize(OpenTypeFont font, params FontVariationSetting[] settings) => [.. font.Variations.CreateInstance(settings).NormalizedCoordinates];

        Assert.Equal([-0.5, 0, 0, 0], Normalize(font, new FontVariationSetting(OpenTypeTag.WidthAxis, 75)));
        Assert.Equal([0.25, 0, 0, 0], Normalize(font, new FontVariationSetting(OpenTypeTag.WidthAxis, 112.5)));
        Assert.Equal([0.75, 0, 0, 0], Normalize(font, new FontVariationSetting(OpenTypeTag.WidthAxis, 150)));
        Assert.Equal([0, 0, 1, -0.5], Normalize(font, new FontVariationSetting(OpenTypeTag.ItalicAxis, 1), new FontVariationSetting(OpenTypeTag.WeightAxis, 250)));
        Assert.Equal([0, 0, 0, 0.4000244140625], Normalize(font, new FontVariationSetting(OpenTypeTag.WeightAxis, 600)));

        // The last value of an axis wins
        Assert.Equal([0, 0, 0, 1], Normalize(font, new FontVariationSetting(OpenTypeTag.WeightAxis, 100), new FontVariationSetting(OpenTypeTag.WeightAxis, 900)));
    }

    [Theory]
    [InlineData(100, 125)]
    [InlineData(900, 75)]
    [InlineData(400, 100)]
    [InlineData(650, 90)]
    public void AxisVariations_Avar2(double weight, double width)
    {
        // DesignSpaceWarpTestAvar2 re-implements the segment maps of DesignSpaceWarpTestAvar1 with the variation store of avar 2
        // (and deltas applied to the axes): the normalized coordinates are the same
        var avar1 = LoadFace(TestPaths.External("wpt", "css", "css-fonts", "resources", "avar", "DesignSpaceWarpTestAvar1[opsz,wdth,wght].ttf"));
        var avar2 = LoadFace(TestPaths.External("wpt", "css", "css-fonts", "resources", "avar", "DesignSpaceWarpTestAvar2[opsz,wdth,wght].ttf"));
        FontVariationSetting[] settings = [new(OpenTypeTag.WeightAxis, weight), new(OpenTypeTag.WidthAxis, width)];
        var expected = avar1.Variations.CreateInstance(settings).NormalizedCoordinates;
        var actual = avar2.Variations.CreateInstance(settings).NormalizedCoordinates;
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i], actual[i], 0.002);
        }
    }

    [Fact]
    public void VariationSequencesAndVerticalMetrics()
    {
        var font = LoadFace(TestPaths.External("wpt", "css", "css-fonts", "resources", "vs", "NotoSansJP-Regular_with-cmap14-subset.ttf"));
        Assert.Equal(1, font.GetGlyphId(0x8279));

        // The non-default UVS mappings, the default UVS (the glyph of the character) and an unsupported sequence
        Assert.True(font.TryGetGlyphId(0x8279, 0xFE00, out var glyph));
        Assert.Equal(2, glyph);
        Assert.True(font.TryGetGlyphId(0x8279, 0xFE01, out glyph));
        Assert.Equal(3, glyph);
        Assert.True(font.TryGetGlyphId(0x8279, 0xE0100, out glyph));
        Assert.Equal(1, glyph);
        Assert.False(font.TryGetGlyphId(0x8279, 0xFE0F, out _));
        Assert.False(font.TryGetGlyphId('A', 0xFE00, out _));

        Assert.True(font.HasVerticalMetrics);
        Assert.Equal(500, font.VerticalAscender);
        Assert.Equal(-500, font.VerticalDescender);
        Assert.Equal(1000, font.GetAdvanceHeight(1));
        Assert.Equal(90, font.GetTopSideBearing(1));
    }

    [Fact]
    public void FontsOfTheWebPlatformTests_AreValid()
    {
        var extensions = new[] { ".ttf", ".otf", ".ttc", ".woff", ".woff2" };
        var count = 0;
        foreach (var path in Directory.EnumerateFiles(TestPaths.External("wpt"), "*", SearchOption.AllDirectories))
        {
            if (!extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                continue;

            // The fonts of the WOFF 2.0 conformance reftests are invalid on purpose (they must be rejected)
            if (path.Replace('\\', '/').Contains("/css/WOFF2/", StringComparison.Ordinal))
                continue;

            var file = FontFile.Load(File.ReadAllBytes(path));
            Assert.True(file is not null, path);
            foreach (var face in file.Faces)
            {
                Exercise(face, isCorrupted: false);
            }

            count++;
        }

        Assert.True(count > 100);
    }

    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x77, 0x4F, 0x46, 0x46, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x77, 0x4F, 0x46, 0x32, 0, 0, 0, 0, 0, 0, 0, 0 })]
    [InlineData(new byte[] { 0x74, 0x74, 0x63, 0x66, 0, 1, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0x74, 0x79, 0x70, 0x31, 0, 0, 0, 0, 0, 0, 0, 0 })]
    public void InvalidData_IsNotAFont(byte[] data)
    {
        Assert.Null(FontFile.Load(data));
    }

    [Theory]
    [InlineData("Ahem.ttf")]
    [InlineData("Tinos-Regular.woff2")]
    [InlineData("Arimo-VF.woff2")]
    public void TruncatedFonts_AreInvalid(string file)
    {
        var data = File.ReadAllBytes(Bundled(file));
        for (var length = 0; length < data.Length - 3; length += Math.Max(1, length / 16))
        {
            // A truncated file is invalid (the length of a WOFF2 file is in its header; the last table of an sfnt file is
            // out of the data, except for its padding)
            Assert.Null(FontFile.Load(data.AsSpan(0, length)));
        }
    }

    [Theory]
    [InlineData("Ahem.ttf")]
    [InlineData("Arimo-VF.woff2")]
    [InlineData("Resources/Ahem.woff")]
    [InlineData("Resources/Ahem-transformed-hmtx.woff2")]
    public void CorruptedFonts_DoNotThrow(string file)
    {
        var data = File.ReadAllBytes(file.StartsWith("Resources/", StringComparison.Ordinal) ? Resource(file["Resources/".Length..]) : Bundled(file));
        var random = new Random(42);
        for (var i = 0; i < 300; i++)
        {
            var corrupted = (byte[])data.Clone();
            for (var j = 0; j < 1 + (i % 8); j++)
            {
#pragma warning disable CA5394 // The corruptions are reproducible (seeded), not secret
                corrupted[random.Next(corrupted.Length)] = (byte)random.Next(256);
#pragma warning restore CA5394
            }

            // A corrupted font is valid or not, but reading it never throws
            if (FontFile.Load(corrupted) is { } font)
            {
                foreach (var face in font.Faces)
                {
                    Exercise(face, isCorrupted: true);
                }
            }
        }
    }

    /// <summary>Reads every kind of data of a face (as the fuzzing target does).</summary>
    private static void Exercise(OpenTypeFont font, bool isCorrupted)
    {
        _ = font.Metrics;
        _ = font.FullName;
        _ = font.GetName(OpenTypeFont.NameId.License);
        var instance = font.Variations.CreateInstance([.. font.Variations.Axes.Select(axis => new FontVariationSetting(axis.Tag, axis.MaxValue))]);
        _ = font.GetMetrics(instance);
        var previous = (ushort)0;
        foreach (var c in "AVTo0x H\U000000E9\U000003B1\U00000416\U00004E00")
        {
            var glyph = font.GetGlyphId(c);
            _ = font.GetAdvanceWidth(glyph, instance);
            _ = font.GetLeftSideBearing(glyph);
            _ = font.GetAdvanceHeight(glyph);
            _ = font.GetTopSideBearing(glyph);
            _ = font.TryGetGlyphBounds(glyph, out _);
            _ = font.TryGetGlyphId(c, 0xFE00, out _);
            _ = font.GetKerning(previous, glyph, instance);
            _ = font.GetPairAdjustment(previous, glyph, OpenTypeTag.VerticalKerning);
            previous = glyph;
        }

        _ = font.GetAdvanceWidth((ushort)(font.GlyphCount + 10));
        _ = font.GetLeftSideBearing(ushort.MaxValue);

        // The shaper with the GSUB, GPOS and GDEF tables of the face: a malformed table of a corrupted font is reported as such
        // (the text shaping then draws the characters without the layout tables)
        Shape("office AVAT\u0301a\u00E9 fi\u200D", UnicodeScript.Latin, isRightToLeft: false, isVertical: false, "tr", instance);
        Shape("\u0644\u0627 \u0628\u0628\u064E\u200C\u0628", UnicodeScript.Arabic, isRightToLeft: true, isVertical: false, "ar", instance);
        Shape("\u1820\u180B\u1821\u180E\u1822", UnicodeScript.Mongolian, isRightToLeft: false, isVertical: true, language: null, instance: null);
        Shape("\u4E00\u3001", UnicodeScript.Han, isRightToLeft: false, isVertical: true, "ja", instance: null);

        void Shape(string text, UnicodeScript script, bool isRightToLeft, bool isVertical, string? language, FontVariationInstance? instance)
        {
            try
            {
                _ = font.Shape(text, script, isRightToLeft, isVertical, language, instance);
            }
            catch (Meziantou.Framework.Fonts.InvalidFontException) when (isCorrupted)
            {
                // The layout tables of the corrupted font are malformed
            }
        }
    }
}
