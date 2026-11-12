using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.VectorGraphics.Tests;

public sealed class TextMeasurerTests
{
    private static readonly AfmTextMeasurer Afm = AfmTextMeasurer.Default;

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void Afm_AsciiTextsMeasureAsTheirGraphemeClusters()
    {
        // An ASCII text is measured without enumerating its grapheme clusters. A leading U+0085, a control without width
        // that is a cluster of its own, makes the measurer enumerate them: the widths must be the same, to the last bit.
        var printable = Enumerable.Range(0x20, 0x5F).Select(c => (char)c).ToArray();
        var random = new Random(42);
        var texts = printable.SelectMany(first => printable.Select(second => string.Concat(first, second)))
            .Concat(Enumerable.Range(0, 1000).Select(i => new string([.. Enumerable.Range(0, random.Next(0, 40)).Select(_ => i % 3 is 0 ? (char)random.Next(0, 0x80) : printable[random.Next(printable.Length)])])))
            .Concat(["A\r\nV", "AV\tAV", "\u007FA", "Revenue (k)", string.Empty]);
        foreach (var font in new SceneFont[] { new(12), new(12, Weight: SceneFont.BoldWeight), new(12, Italic: true), new(12, Weight: SceneFont.BoldWeight, Italic: true) })
        {
            foreach (var text in texts)
            {
                Assert.Equal(Afm.Measure("\u0085" + text, font).Width, Afm.Measure(text, font).Width);
            }
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Generating test inputs, and the fixed seed keeps the cases reproducible.")]
    public void TextFitting_AsciiGraphemeClustersAreTheCharactersExceptCrLf()
    {
        var random = new Random(42);
        var texts = Enumerable.Range(0, 1000)
            .Select(i => new string([.. Enumerable.Range(0, random.Next(0, 30)).Select(_ => i % 2 is 0 ? "\r\n a"[random.Next(4)] : (char)random.Next(0, 0x80))]))
            .Concat(["\r\n", "\r\r\n\n", "a\r", "\n\r"]);
        foreach (var text in texts)
        {
            var expected = new List<int>();
            foreach (var range in Unicode.EnumerateGraphemeClusters(text))
            {
                expected.Add(range.End.GetOffset(text.Length));
            }

            Assert.Equal(expected, TextFitting.GetGraphemeClusterEnds(text));
        }
    }

    [Fact]
    public void Afm_UsesTheWidthsAndTheKerningOfHelvetica()
    {
        // A and V are 667 units wide, and the pair AV is kerned by -70 units (Helvetica.afm)
        Assert.Equal(6.67, Afm.Measure("A", new SceneFont(10)).Width, 1e-9);
        Assert.Equal(12.64, Afm.Measure("AV", new SceneFont(10)).Width, 1e-9);
        Assert.Equal(7.22, Afm.Measure("A", new SceneFont(10, Weight: SceneFont.BoldWeight)).Width, 1e-9);
        Assert.Equal(0, Afm.Measure(string.Empty, new SceneFont(10)).Width);

        var metrics = Afm.Measure("x", new SceneFont(10));
        Assert.Equal(7.18, metrics.Ascent, 1e-9);
        Assert.Equal(2.07, metrics.Descent, 1e-9);
        Assert.Equal(9.25, metrics.Height, 1e-9);
        Assert.Equal("Helvetica, Arial, sans-serif", Afm.FontFamily);
    }

    [Fact]
    public void Afm_FallbackForCharactersTheFontLacks()
    {
        var font = new SceneFont(10);

        // Wide characters (CJK, most emoji) are 1 em, combining marks nothing, the others the average width
        Assert.Equal(10, Afm.Measure("中", font).Width, 1e-9);
        Assert.Equal(20, Afm.Measure("日本", font).Width, 1e-9);
        Assert.Equal(10, Afm.Measure("😀", font).Width, 1e-9);
        Assert.Equal(Afm.Measure("e", font).Width, Afm.Measure("e\u0301", font).Width, 1e-9);
        Assert.Equal(0, Afm.Measure("\u0301", font).Width);
        Assert.Equal(0, Afm.Measure("\u0007\u200B", font).Width);
        var average = Afm.Measure("ж", font).Width;
        Assert.InRange(average, 4, 7);
        Assert.Equal(average * 2, Afm.Measure("жж", font).Width, 1e-9);

        // A family emoji is one grapheme cluster
        Assert.Equal(10, Afm.Measure("\U0001F468\u200D\U0001F469\u200D\U0001F467", font).Width, 1e-9);
    }

    [Fact]
    public void Afm_MeasuresItalicTextWithTheObliqueMetrics()
    {
        // Helvetica-Oblique has the widths of Helvetica, Helvetica-BoldOblique the ones of Helvetica-Bold
        Assert.Equal(Afm.Measure("Diagram", new SceneFont(10)).Width, Afm.Measure("Diagram", new SceneFont(10, Italic: true)).Width, 1e-9);
        Assert.Equal(Afm.Measure("Diagram", new SceneFont(10, Weight: SceneFont.BoldWeight)).Width, Afm.Measure("Diagram", new SceneFont(10, Weight: SceneFont.BoldWeight, Italic: true)).Width, 1e-9);
        Assert.NotEqual(Afm.Measure("Diagram", new SceneFont(10)).Width, Afm.Measure("Diagram", new SceneFont(10, Weight: SceneFont.BoldWeight)).Width);
    }

    [Fact]
    public void FontFace_UsesTheFaceOfTheStyle()
    {
        var environment = new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024);
        var regular = FontLoader.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationSans-Regular.woff2")), environment)[0];
        var italic = FontLoader.Load(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationSans-Italic.woff2")), environment)[0];
        var measurer = new FontFaceTextMeasurer(regular, bold: null, italic, boldItalic: null);

        // Liberation Sans has the widths of Helvetica, in every style
        Assert.Equal(Afm.Measure("Diagram", new SceneFont(12, Italic: true)).Width, measurer.Measure("Diagram", new SceneFont(12, Italic: true)).Width, 0.5);
        Assert.Equal(measurer.Measure("abc", new SceneFont(10, Italic: true)).Width, measurer.Measure("abc", new SceneFont(10, Weight: SceneFont.BoldWeight, Italic: true)).Width);
        Assert.Equal("Liberation Sans", measurer.FontFamily);
    }

    [Fact]
    public void Measure_AddsTheSpacing()
    {
        var font = new SceneFont(10);
        var width = Afm.Measure("a b", font).Width;

        // The letter spacing after each of the 3 characters, the word spacing after the space
        Assert.Equal(width + 3 + 2, Afm.Measure("a b", font, letterSpacing: 1, wordSpacing: 2).Width, 1e-9);
        Assert.Equal(Afm.Measure("a\u00A0b", font).Width + 2, Afm.Measure("a\u00A0b", font, letterSpacing: 0, wordSpacing: 2).Width, 1e-9);
        Assert.Equal(width, Afm.Measure("a b", font, double.NaN, double.PositiveInfinity).Width, 1e-9);

        // A text applies its spacing to the spans that set none
        var text = new SceneText(0, 0, [new SceneTextSpan("ab"), new SceneTextSpan("c") { LetterSpacing = 5 }], font, SceneColor.Black) { LetterSpacing = 1 };
        Assert.Equal(Afm.Measure("ab", font).Width + 2 + Afm.Measure("c", font).Width + 5, Afm.Measure(text).Width, 1e-9);
        Assert.Equal(Afm.Measure("ab", font).Width + Afm.Measure("c", font).Width + 5, Afm.Measure(text.Spans, font).Width, 1e-9);
        Assert.Equal(width + 3, Afm.Measure(new SceneText(0, 0, "a b", font, SceneColor.Black) { LetterSpacing = 1 }).Width, 1e-9);
    }

    [Fact]
    public void Measure_SpacingOfShapedTexts()
    {
        // With a letter spacing, the optional ligatures are not formed: each of the 10 characters gets its spacing
        var measurer = new FontFaceTextMeasurer(SceneFontCollection.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationSans-Regular.woff2")));
        var font = new SceneFont(20);
        var once = measurer.Measure("ffi office", font, letterSpacing: 1, wordSpacing: 0).Width;
        var twice = measurer.Measure("ffi office", font, letterSpacing: 2, wordSpacing: 0).Width;
        Assert.Equal(10, twice - once, 1e-9);

        // The word spacing goes after the space only, and a measurer that renames the family keeps the shaping
        Assert.Equal(3, measurer.Measure("ffi office", font, letterSpacing: 1, wordSpacing: 3).Width - once, 1e-9);
        Assert.Equal(once, measurer.WithFontFamily("Other").Measure("ffi office", font, letterSpacing: 1, wordSpacing: 0).Width, 1e-9);
        Assert.Equal(measurer.Measure("ffi office", font).Width, measurer.Measure("ffi office", font, 0, 0).Width);
    }

    [Fact]
    public void Truncate()
    {
        var font = new SceneFont(10);
        Assert.Equal("Hello", TextFitting.Truncate(Afm, "Hello", font, 100));
        var truncated = TextFitting.Truncate(Afm, "Hello world, how are you", font, 50)!;
        Assert.EndsWith("…", truncated);
        Assert.True(Afm.Measure(truncated, font).Width <= 50);
        Assert.True(Afm.Measure(truncated[..^1] + "x…", font).Width > 50 || truncated.Length < 3);
        Assert.Null(TextFitting.Truncate(Afm, "Hello", font, 1));

        // A grapheme cluster is never split
        Assert.DoesNotContain("\uD83D…", TextFitting.Truncate(Afm, "ab😀😀😀😀", font, 25)!);
    }

    [Fact]
    public void Wrap_Example()
    {
        #region VectorGraphics_Wrap
        var measurer = AfmTextMeasurer.Default;
        var font = new SceneFont(12);

        // At most 100 pixels per line, broken between words, and at the line feeds of the text
        var lines = TextFitting.Wrap(measurer, "A label that is too long for its box\nSecond paragraph", font, maxWidth: 100);
        Assert.Equal(["A label that is too", "long for its box", "Second paragraph"], lines);

        // Or shortened with an ellipsis
        Assert.Equal("A label that i…", TextFitting.Truncate(measurer, "A label that is too long", font, maxWidth: 80));
        #endregion
    }

    [Fact]
    public void Wrap_BreaksBetweenWords()
    {
        var font = new SceneFont(10);
        var lines = TextFitting.Wrap(Afm, "The quick brown fox jumps over the lazy dog", font, 60);
        Assert.True(lines.Count > 1);
        Assert.Equal("The quick brown fox jumps over the lazy dog", string.Join(' ', lines));
        foreach (var line in lines)
        {
            Assert.True(Afm.Measure(line, font).Width <= 60, line);
            Assert.False(line.EndsWith(' ', StringComparison.Ordinal));
        }

        // The longest prefix that fits is kept on each line
        for (var i = 0; i < lines.Count - 1; i++)
        {
            var next = lines[i + 1].Split(' ')[0];
            Assert.True(Afm.Measure(lines[i] + " " + next, font).Width > 60);
        }
    }

    [Fact]
    public void Wrap_KeepsTheLineFeeds()
    {
        var font = new SceneFont(10);
        Assert.Equal(["first", "second"], TextFitting.Wrap(Afm, "first\nsecond", font, 1000));
        Assert.Equal(["first", "", "third"], TextFitting.Wrap(Afm, "first\n\nthird", font, 1000));
        Assert.Equal(["a", "b"], TextFitting.Wrap(Afm, "a\r\nb", font, 1000));
        Assert.Equal(["first second"], TextFitting.Wrap(Afm, "first second", font, double.PositiveInfinity));
        Assert.Equal(["first second"], TextFitting.Wrap(Afm, "first second", font, 0));
        Assert.Equal([string.Empty], TextFitting.Wrap(Afm, string.Empty, font, 100));
    }

    [Fact]
    public void Wrap_BreaksLongWordsBetweenGraphemeClusters()
    {
        var font = new SceneFont(10);
        var lines = TextFitting.Wrap(Afm, "Supercalifragilisticexpialidocious", font, 40);
        Assert.True(lines.Count > 1);
        Assert.Equal("Supercalifragilisticexpialidocious", string.Concat(lines));
        foreach (var line in lines)
        {
            Assert.True(Afm.Measure(line, font).Width <= 40, line);
        }

        // A cluster wider than the line stays whole, alone on its line
        Assert.Equal(["😀", "😀"], TextFitting.Wrap(Afm, "😀😀", font, 2));
        Assert.Equal(["e\u0301", "e\u0301"], TextFitting.Wrap(Afm, "e\u0301e\u0301", font, 6));

        // The text after the long word continues after its last piece
        lines = TextFitting.Wrap(Afm, "Supercalifragilisticexpialidocious is long\nend", font, 40);
        Assert.Equal("end", lines[^1]);
        Assert.Equal("long", lines[^2]);
        Assert.EndsWith(" is", lines[^3], StringComparison.Ordinal);
        foreach (var line in lines)
        {
            Assert.True(Afm.Measure(line, font).Width <= 40, line);
        }
    }

    private static SceneFontFace LoadFace(string fileName) => SceneFontFace.Load(Path.Combine(AppContext.BaseDirectory, "TestData", fileName))[0];

    [Fact]
    public void SceneFontFace_ReadsTheFamilyAndTheStyle()
    {
        var bold = LoadFace("LiberationSans-Bold.woff2");
        Assert.Equal("Liberation Sans", bold.FamilyName);
        Assert.Equal(700, bold.Weight);
        Assert.False(bold.IsItalic);
        Assert.Equal(0, bold.Index);
        Assert.True(LoadFace("LiberationSans-Italic.woff2").IsItalic);

        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationMono-Regular.woff2"));
        Assert.Equal(bytes, SceneFontFace.Load(bytes)[0].Data.ToArray());
        using var stream = new MemoryStream(bytes);
        Assert.Equal("Liberation Mono", SceneFontFace.Load(stream)[0].FamilyName);

        Assert.Throws<UnknownFontFormatException>(() => SceneFontFace.Load("not a font"u8));
        Assert.Throws<ArgumentNullException>("stream", () => SceneFontFace.Load((Stream)null!));
    }

    [Fact]
    public void SceneFontCollection_ResolvesTheFamilyAndTheStyle()
    {
        var sans = LoadFace("LiberationSans-Regular.woff2");
        var sansBold = LoadFace("LiberationSans-Bold.woff2");
        var sansItalic = LoadFace("LiberationSans-Italic.woff2");
        var mono = LoadFace("LiberationMono-Regular.woff2");
        var fonts = new SceneFontCollection(sans, sansBold, sansItalic, mono);

        Assert.Equal(["Liberation Sans", "Liberation Mono"], fonts.Families);
        Assert.Equal("Liberation Sans", fonts.DefaultFamily);
        Assert.Equal("Liberation Sans, Liberation Mono", fonts.FontFamily);
        Assert.True(fonts.ContainsFamily("liberation mono"));
        Assert.False(fonts.ContainsFamily("Arial"));

        // No family, an unknown family, or a family of the collection; the names ignore the case of ASCII letters
        Assert.Same(sans, fonts.GetFace(new SceneFont(10)));
        Assert.Same(sans, fonts.GetFace(new SceneFont(10, Family: "Arial, sans-serif")));
        Assert.Same(mono, fonts.GetFace(new SceneFont(10, Family: "liberation mono")));
        Assert.Same(mono, fonts.GetFace(new SceneFont(10, Family: "'Unknown', \"Liberation Mono\", serif")));
        Assert.Same(mono, fonts.GetFace(new SceneFont(10), defaultFamily: "Liberation   Mono"));
        Assert.Same(sans, fonts.GetFace(new SceneFont(10, Family: "Liberation Sans"), defaultFamily: "Liberation Mono"));

        // The closest style: the slant first, then the weight
        Assert.Same(sansBold, fonts.GetFace(new SceneFont(10, Weight: SceneFont.BoldWeight)));
        Assert.Same(sansItalic, fonts.GetFace(new SceneFont(10, Italic: true)));
        Assert.Same(sansItalic, fonts.GetFace(new SceneFont(10, Weight: SceneFont.BoldWeight, Italic: true)));
        Assert.Same(mono, fonts.GetFace(new SceneFont(10, Weight: SceneFont.BoldWeight, Family: "Liberation Mono")));

        // The other families follow, for the characters the family does not have
        Assert.Equal([mono, sansBold], fonts.GetFaces(new SceneFont(10, Weight: SceneFont.BoldWeight, Family: "Liberation Mono")));
        Assert.Same(mono, new SceneFontCollection([sans, mono], defaultFamily: "LIBERATION MONO").GetFace(new SceneFont(10)));
    }

    [Fact]
    public void SceneFontCollection_WeightsSelectTheNearestFace()
    {
        var regular = LoadFace("LiberationSans-Regular.woff2");
        var bold = LoadFace("LiberationSans-Bold.woff2");
        var fonts = new SceneFontCollection(regular, bold);
        Assert.Same(regular, fonts.GetFace(new SceneFont(10, Weight: 100)));
        Assert.Same(regular, fonts.GetFace(new SceneFont(10, Weight: 549)));
        Assert.Same(bold, fonts.GetFace(new SceneFont(10, Weight: 551)));
        Assert.Same(bold, fonts.GetFace(new SceneFont(10, Weight: 2000)));

        // The lines of the text: the values of the font, in em
        Assert.InRange(regular.UnderlinePosition, -0.2, 0);
        Assert.InRange(regular.UnderlineThickness, 0.01, 0.2);
        Assert.InRange(regular.StrikeoutPosition, 0.1, 0.5);
        Assert.InRange(regular.StrikeoutThickness, 0.01, 0.2);
    }

    [Fact]
    public void SceneFontCollection_EmojiPresentationChoosesTheFace()
    {
        var sans = LoadFace("LiberationSans-Regular.woff2");
        var emoji = LoadFace("Noto-COLRv1.subset.ttf");
        Assert.True(emoji.Face.HasColorGlyphs);
        var fonts = new SceneFontCollection(sans, emoji);

        // An emoji both faces have: the text face draws it, unless a selector asks for the emoji presentation
        var codePoint = Enumerable.Range(0x20, 0x1FFFF - 0x20).FirstOrDefault(value => (value is < 0xD800 or > 0xDFFF) && UnicodeEmoji.IsEmoji(value) && !UnicodeEmoji.HasEmojiPresentation(value) && sans.Face.MapCodePoint(value) != 0 && emoji.Face.MapCodePoint(value) != 0);
        Assert.NotEqual(0, codePoint);
        var character = char.ConvertFromUtf32(codePoint);
        Assert.Same(sans, Assert.Single(fonts.GetRuns(character, new SceneFont(10))).Face);
        Assert.Same(emoji, Assert.Single(fonts.GetRuns(character + "\uFE0F", new SceneFont(10))).Face);
        Assert.Same(sans, Assert.Single(fonts.GetRuns(character + "\uFE0E", new SceneFont(10, Family: emoji.FamilyName))).Face);
    }

    [Fact]
    public void SceneFontFace_ReadsAtMostTheLimit()
    {
        // A stream that never ends is read up to one byte more than the limit, which the font engine refuses
        using var stream = new EndlessStream();
        Assert.HasCount(11, SceneFontFace.ReadBounded(stream, 10));
    }

    private sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            buffer.AsSpan(offset, count).Fill(1);
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData("Liberation\\ Mono")]
    [InlineData("\"Liberation\\20 Mono\"")]
    [InlineData("'Liberation\\000020Mono'")]
    [InlineData("Unknown, Liberation\\ Mono")]
    public void SceneFontCollection_FamilyNamesDecodeTheEscapesOfCss(string family)
    {
        var mono = LoadFace("LiberationMono-Regular.woff2");
        var fonts = new SceneFontCollection(LoadFace("LiberationSans-Regular.woff2"), mono);
        Assert.Same(mono, fonts.GetFace(new SceneFont(10, Family: family)));
    }

    [Fact]
    public void SceneFontCollection_InvalidArguments()
    {
        var sans = LoadFace("LiberationSans-Regular.woff2");
        Assert.Throws<ArgumentException>("faces", () => new SceneFontCollection());
        Assert.Throws<ArgumentException>("faces", () => new SceneFontCollection(sans, null!));
        Assert.Throws<ArgumentException>("defaultFamily", () => new SceneFontCollection([sans], "Liberation Mono"));
        Assert.Throws<ArgumentNullException>("faces", () => new SceneFontCollection((IEnumerable<SceneFontFace>)null!));
    }

    [Fact]
    public void SceneFontCollection_MeasuresEachTextWithItsFamily()
    {
        var fonts = new SceneFontCollection(LoadFace("LiberationSans-Regular.woff2"), LoadFace("LiberationMono-Regular.woff2"));
        var measurer = new FontFaceTextMeasurer(fonts);
        Assert.Same(fonts, measurer.Fonts);
        Assert.Same(fonts, ((ITextMeasurer)measurer).Fonts);
        Assert.Equal(fonts.FontFamily, measurer.FontFamily);
        Assert.Null(((ITextMeasurer)Afm).Fonts);

        // Liberation Mono is 0.6 em per character; Liberation Sans has the widths of Helvetica
        Assert.Equal(60, measurer.Measure("iiiiiiiiii", new SceneFont(10, Family: "Liberation Mono")).Width, 0.05);
        Assert.Equal(Afm.Measure("iiiiiiiiii", new SceneFont(10)).Width, measurer.Measure("iiiiiiiiii", new SceneFont(10)).Width, 0.05);

        // A measurer with another family measures the texts that name no family with it
        var monospace = measurer.WithFontFamily("Liberation Mono");
        Assert.Equal("Liberation Mono", monospace.FontFamily);
        Assert.Same(fonts, monospace.Fonts);
        Assert.Equal(60, monospace.Measure("iiiiiiiiii", new SceneFont(10)).Width, 0.05);
        Assert.Equal(measurer.Measure("iiiiiiiiii", new SceneFont(10)).Width, monospace.Measure("iiiiiiiiii", new SceneFont(10, Family: "Liberation Sans")).Width);
        Assert.Same(measurer, measurer.WithFontFamily(null));
        Assert.Same(measurer, measurer.WithFontFamily(measurer.FontFamily));
        Assert.Equal(60, monospace.WithFontFamily("Arial").WithFontFamily("Liberation Mono").Measure("iiiiiiiiii", new SceneFont(10)).Width, 0.05);
    }

    [Fact]
    public void SceneFontCollection_MeasuresTheCharactersAFamilyDoesNotHaveWithTheNextFamily()
    {
        var sans = LoadFace("LiberationSans-Regular.woff2");
        var thai = LoadFace("NotoSansThai-Regular.ttf");
        var fonts = new SceneFontCollection(sans, thai);
        var measurer = new FontFaceTextMeasurer(fonts);
        var font = new SceneFont(20);

        // Liberation Sans has no Thai character: they are measured with Noto Sans Thai
        var latin = new FontFaceTextMeasurer(new SceneFontCollection(sans)).Measure("ab ", font).Width;
        var thaiOnly = new FontFaceTextMeasurer(new SceneFontCollection(thai)).Measure("\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", font).Width;
        Assert.Equal(latin + thaiOnly, measurer.Measure("ab \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", font).Width, 1e-9);
        Assert.Equal(
            [("ab ", "Liberation Sans"), ("\u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", "Noto Sans Thai")],
            fonts.GetRuns("ab \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35", font).Select(run => ("ab \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35".Substring(run.Start, run.Length), run.Face.FamilyName)));

        // The ascent and the descent are the ones of the family of the text
        Assert.Equal(new FontFaceTextMeasurer(new SceneFontCollection(sans)).Measure("a", font).Ascent, measurer.Measure("\u0E2A", font).Ascent);

        // A character no family has is measured with the family of the text
        Assert.Single(fonts.GetRuns("a\u4E2Db", font));
        Assert.Empty(fonts.GetRuns("", font));
        Assert.Throws<ArgumentNullException>("text", () => fonts.GetRuns(null!, font));
        Assert.Equal(0, measurer.Measure("", font).Width);
    }
}
