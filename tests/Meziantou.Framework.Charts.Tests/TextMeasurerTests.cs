using Meziantou.Framework.Fonts;

namespace Meziantou.Framework.Charts.Tests;

public sealed class TextMeasurerTests
{
    private static readonly AfmTextMeasurer Afm = AfmTextMeasurer.Default;

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
            Assert.Equal(Afm.Measure(text, new SceneFont(12)).Width, measurer.Measure(text, new SceneFont(12)).Width, 0.5);
        }

        var metrics = measurer.Measure("x", new SceneFont(10));
        Assert.True(metrics.Ascent > 8);
        Assert.True(metrics.Descent > 1);
        Assert.Equal(0, measurer.Measure(string.Empty, new SceneFont(10)).Width);
        Assert.Equal(measurer.Measure("abc", new SceneFont(10)).Width, measurer.Measure("abc", new SceneFont(10, Bold: true)).Width);

        // Mixed scripts and right-to-left text are shaped run by run
        Assert.True(measurer.Measure("abc שלום 123", new SceneFont(10)).Width > 0);
        Assert.True(measurer.Measure("123 مرحبا", new SceneFont(10)).Width > 0);
        Assert.Equal("Liberation Sans", new FontFaceTextMeasurer(face).FontFamily);
        Assert.Throws<ArgumentNullException>("regular", () => new FontFaceTextMeasurer(null!));
        Assert.Throws<ArgumentNullException>("text", () => measurer.Measure(null!, new SceneFont(10)));
    }

    [Fact]
    public void FitsInCircle()
    {
        Assert.True(LabelFitting.FitsInCircle(new TextMetrics(6, 3, 1), 5));
        Assert.False(LabelFitting.FitsInCircle(new TextMetrics(10, 3, 1), 5));
    }
}
