using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Charts.Tests;

public sealed class TextMeasurerTests
{
    private static readonly AfmTextMeasurer Afm = AfmTextMeasurer.Default;

    [Fact]
    public void Afm_UsesTheWidthsAndTheKerningOfHelvetica()
    {
        // A and V are 667 units wide, and the pair AV is kerned by -70 units (Helvetica.afm)
        Assert.Equal(6.67, Afm.Measure("A", new ChartFont(10)).Width, 1e-9);
        Assert.Equal(12.64, Afm.Measure("AV", new ChartFont(10)).Width, 1e-9);
        Assert.Equal(7.22, Afm.Measure("A", new ChartFont(10, Bold: true)).Width, 1e-9);
        Assert.Equal(0, Afm.Measure(string.Empty, new ChartFont(10)).Width);

        var metrics = Afm.Measure("x", new ChartFont(10));
        Assert.Equal(7.18, metrics.Ascent, 1e-9);
        Assert.Equal(2.07, metrics.Descent, 1e-9);
        Assert.Equal(9.25, metrics.Height, 1e-9);
        Assert.Equal("Helvetica, Arial, sans-serif", Afm.FontFamily);
    }

    [Fact]
    public void Afm_FallbackForCharactersTheFontLacks()
    {
        var font = new ChartFont(10);

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
    public void FontFace_ShapesTheText()
    {
        #region Charts_FontFaceTextMeasurer
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "LiberationSans-Regular.woff2"));
        var face = FontLoader.Load(bytes, new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024))[0];
        var measurer = new FontFaceTextMeasurer(face, fontFamily: "'Liberation Sans', Arial, sans-serif");

        var plot = new Plot { Title = "Measured with Liberation Sans", TextMeasurer = measurer };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount));
        var svg = plot.ToSvg();
        #endregion

        Assert.Contains("font-family=\"&apos;Liberation Sans&apos;, Arial, sans-serif\"".Replace("&apos;", "'", StringComparison.Ordinal), svg);
        ChartAssert.Svg(svg);

        // Liberation Sans has the widths of Helvetica
        foreach (var text in new[] { "Sales by region", "0123456789" })
        {
            Assert.Equal(Afm.Measure(text, new ChartFont(12)).Width, measurer.Measure(text, new ChartFont(12)).Width, 0.5);
        }

        var metrics = measurer.Measure("x", new ChartFont(10));
        Assert.True(metrics.Ascent > 8);
        Assert.True(metrics.Descent > 1);
        Assert.Equal(0, measurer.Measure(string.Empty, new ChartFont(10)).Width);
        Assert.Equal(measurer.Measure("abc", new ChartFont(10)).Width, measurer.Measure("abc", new ChartFont(10, Bold: true)).Width);

        // Mixed scripts and right-to-left text are shaped run by run
        Assert.True(measurer.Measure("abc שלום 123", new ChartFont(10)).Width > 0);
        Assert.True(measurer.Measure("123 مرحبا", new ChartFont(10)).Width > 0);
        Assert.Equal("Liberation Sans", new FontFaceTextMeasurer(face).FontFamily);
        Assert.Throws<ArgumentNullException>("regular", () => new FontFaceTextMeasurer(null!));
        Assert.Throws<ArgumentNullException>("text", () => measurer.Measure(null!, new ChartFont(10)));
    }

    [Fact]
    public void Truncate()
    {
        var font = new ChartFont(10);
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
    public void FitsInCircle()
    {
        Assert.True(TextFitting.FitsInCircle(new TextMetrics(6, 3, 1), 5));
        Assert.False(TextFitting.FitsInCircle(new TextMetrics(10, 3, 1), 5));
    }
}
