using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.VectorGraphics.Tests;

public sealed class TextMeasurerTests
{
    private static readonly AfmTextMeasurer Afm = AfmTextMeasurer.Default;

    [Fact]
    public void Afm_UsesTheWidthsAndTheKerningOfHelvetica()
    {
        // A and V are 667 units wide, and the pair AV is kerned by -70 units (Helvetica.afm)
        Assert.Equal(6.67, Afm.Measure("A", new SceneFont(10)).Width, 1e-9);
        Assert.Equal(12.64, Afm.Measure("AV", new SceneFont(10)).Width, 1e-9);
        Assert.Equal(7.22, Afm.Measure("A", new SceneFont(10, Bold: true)).Width, 1e-9);
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
        Assert.Equal(Afm.Measure("Diagram", new SceneFont(10, Bold: true)).Width, Afm.Measure("Diagram", new SceneFont(10, Bold: true, Italic: true)).Width, 1e-9);
        Assert.NotEqual(Afm.Measure("Diagram", new SceneFont(10)).Width, Afm.Measure("Diagram", new SceneFont(10, Bold: true)).Width);
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
        Assert.Equal(measurer.Measure("abc", new SceneFont(10, Italic: true)).Width, measurer.Measure("abc", new SceneFont(10, Bold: true, Italic: true)).Width);
        Assert.Equal("Liberation Sans", measurer.FontFamily);
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
}
