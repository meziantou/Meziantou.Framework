using System.Globalization;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.PieDiagrams;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class PieChartTests
{
    [Fact]
    public void PieChart()
    {
        #region Mermaid_PieChart
        var diagram = new PieChart { Title = "Pets adopted by volunteers", ShowData = true };
        diagram.AddSlice("Dogs", 386);
        diagram.AddSlice("Cats", 85);
        diagram.AddSlice("Rats", 15);
        #endregion

        Assert.Equal(["Dogs", "Cats", "Rats"], diagram.Slices.Select(slice => slice.Label));
        var svg = diagram.ToSvg();
        Assert.Contains(">Dogs [386]<", svg, StringComparison.Ordinal);
        Assert.Contains(">79%<", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ececff\"", svg, StringComparison.Ordinal);
        ChartAssert.Svg(diagram, "PieChart");
    }

    [Fact]
    public void ToPlot()
    {
        #region Mermaid_ToPlot
        var chart = (PieChart)MermaidDocument.Parse("""
            pie title Pets adopted by volunteers
                "Dogs" : 386
                "Cats" : 85
                "Rats" : 15
            """).Content;

        var plot = chart.ToPlot();
        plot.Subtitle = "Source: the shelter, 2025";
        plot.Width = 600;
        var svg = plot.ToSvg();
        #endregion

        Assert.Equal("Pets adopted by volunteers", plot.Title);
        Assert.Contains(">Source: the shelter, 2025<", svg, StringComparison.Ordinal);
        Assert.Contains("width=\"600\"", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void ToPlotReturnsANewPlot()
    {
        var chart = new PieChart();
        chart.AddSlice("A", 1);
        var plot = chart.ToPlot();
        plot.Title = "Changed";
        Assert.NotSame(plot, chart.ToPlot());
        Assert.Null(chart.ToPlot().Title);
    }

    [Fact]
    public void DonutHighlightAndColors()
    {
        var diagram = new PieChart
        {
            DonutHole = 0.4,
            TextPosition = 0.5,
            HighlightedSlice = "Potassium",
            StrokeColor = SceneColor.White,
            StrokeWidth = 1,
            OuterStrokeColor = SceneColor.Parse("#123456"),
            OuterStrokeWidth = 5,
            Opacity = 1,
            SectionTextSize = 12,
            SectionTextColor = SceneColor.Parse("#ff0000"),
            LegendTextSize = 14,
            LegendTextColor = SceneColor.Parse("#1f77b4"),
        };
        diagram.SliceColors[0] = SceneColor.Parse("#ff7f0e");
        diagram.AddSlice("Calcium", 42.96);
        diagram.AddSlice("Potassium", 50.05);
        diagram.AddSlice("Magnesium", 10.01);
        diagram.AddSlice("Iron", 5);

        var svg = diagram.ToSvg();
        Assert.Contains("class=\"pieCircle highlighted\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ff7f0e\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#123456\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ff0000\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#1f77b4\"", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void LabelsEqualOnOneLineKeepTheirColors()
    {
        var diagram = new PieChart();
        diagram.AddSlice("A\nB", 1);
        diagram.AddSlice("A B", 1);
        var scale = Assert.IsType<OrdinalColorScale>(diagram.ToPlot().ColorScale);
        Assert.Equal(["A B", "A B\u200B"], scale.Domain);

        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"" + scale.Palette![0].ToString() + "\"", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fill=\"" + scale.Palette[1].ToString() + "\"", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LegendPositions()
    {
        Assert.Equal(LegendPosition.Right, Create(PieLegendPosition.Right).ToPlot().Legend.Position);
        Assert.Equal(LegendPosition.Top, Create(PieLegendPosition.Top).ToPlot().Legend.Position);
        Assert.Equal(LegendPosition.Bottom, Create(PieLegendPosition.Bottom).ToPlot().Legend.Position);

        // Meziantou.Framework.Charts draws no legend on the left or over the pie: they are on the right
        Assert.Equal(LegendPosition.Right, Create(PieLegendPosition.Left).ToPlot().Legend.Position);
        Assert.Equal(LegendPosition.Right, Create(PieLegendPosition.Center).ToPlot().Legend.Position);

        ChartAssert.Svg(Create(PieLegendPosition.Right));
        ChartAssert.Svg(Create(PieLegendPosition.Top));
        ChartAssert.Svg(Create(PieLegendPosition.Bottom));

        static PieChart Create(PieLegendPosition position)
        {
            var diagram = new PieChart { LegendPosition = position, Theme = DiagramTheme.Dark };
            for (var i = 0; i < 13; i++)
            {
                diagram.AddSlice("Slice " + i.ToString(CultureInfo.InvariantCulture), i + 1);
            }

            return diagram;
        }
    }

    [Fact]
    public void SmallSlicesAreInTheLegendOnly()
    {
        var diagram = new PieChart { Theme = DiagramTheme.Forest };
        diagram.AddSlice("Big", 1000);
        diagram.AddSlice("Tiny", 1);
        diagram.AddSlice("Zero", 0);
        diagram.AddSlice("Big", 5);

        // The circle around the pie, and one slice
        var svg = diagram.ToSvg();
        Assert.Equal(2, CountOccurrences(svg, "<path"));
        Assert.Contains(">Tiny<", svg, StringComparison.Ordinal);
        Assert.Contains(">Zero<", svg, StringComparison.Ordinal);
        Assert.Contains(">100%<", svg, StringComparison.Ordinal);

        // A label keeps its first value, as in Mermaid
        Assert.Equal(1, CountOccurrences(svg, ">Big<"));
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void EmptyAndZeroPies()
    {
        ChartAssert.Svg(new PieChart());

        var zero = new PieChart { DonutHole = 0.5, Theme = DiagramTheme.Neutral, ShowData = true };
        zero.AddSlice("A", 0);
        zero.AddSlice("B", 0);
        var svg = zero.ToSvg();
        Assert.Contains(">A [0]<", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("%<", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void EdgeCasesDrawFiniteCoordinates()
    {
        var huge = new PieChart { ShowData = true };
        huge.AddSlice("Max", double.MaxValue);
        huge.AddSlice("Also max", double.MaxValue);
        huge.AddSlice("Small", 1);
        var svg = huge.ToSvg();
        Assert.Equal(2, CountOccurrences(svg, ">50%<"));
        Assert.Contains(">Max [1.79769", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);

        var single = new PieChart { DonutHole = 0.9, TextPosition = 0, HighlightedSlice = "" };
        single.AddSlice("", 42);
        svg = single.ToSvg();
        Assert.Contains(">100%<", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);

        var many = new PieChart { TextPosition = 1, Opacity = 0, StrokeWidth = 0, OuterStrokeWidth = 0, LegendPosition = PieLegendPosition.Bottom };
        for (var i = 0; i < 40; i++)
        {
            many.AddSlice("Slice with a very long label to truncate in the legend\nnumber " + i.ToString(CultureInfo.InvariantCulture), i);
        }

        many.HighlightedSlice = many.Slices[^1].Label;
        ChartAssert.Svg(many);
    }

    [Fact]
    public void ThemeOfItsOwnDerivesTheColors()
    {
        var diagram = new PieChart { Theme = DiagramTheme.Default with { PrimaryColor = SceneColor.Parse("#ff0000") } };
        diagram.AddSlice("A", 1);
        diagram.AddSlice("B", 1);
        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"#ff0000\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ColorsOfTheThemes()
    {
        var chart = new PieChart { Theme = DiagramTheme.Dark };
        var colors = new PieChartColors(chart);
        Assert.Equal(SceneColor.Parse("#0b0000"), colors.Slice(0));
        Assert.Equal(SceneColor.Black, colors.Slice(11));
        Assert.Equal(SceneColor.Parse("#0b0000"), colors.Slice(12));
        Assert.Equal(SceneColor.Parse("#cccccc"), colors.SectionText);
        Assert.Equal(SceneColor.Parse("#d3d3d3"), colors.LegendText);

        chart.SliceColors[1] = SceneColor.Parse("#abcdef");
        chart.SliceColors[20] = SceneColor.Parse("#123456");
        colors = new PieChartColors(chart);
        Assert.Equal(SceneColor.Parse("#abcdef"), colors.Slice(13));
        Assert.Equal(SceneColor.Parse("#3f5258"), colors.Slice(2));
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(386.0, "386")]
    [InlineData(42.96, "42.96")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(1e21, "1e+21")]
    [InlineData(1.5e300, "1.5e+300")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    public void NumbersAreWrittenAsJavaScript(double value, string expected)
    {
        Assert.Equal(expected, JavaScriptNumber.ToString(value));
    }

    [Fact]
    public void ValuesAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieSlice("a", -1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieSlice("a", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieSlice("a", 1) { Value = double.PositiveInfinity });
        Assert.Throws<ArgumentNullException>("label", () => new PieSlice(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieChart { DonutHole = 0.95 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieChart { TextPosition = 1.5 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieChart { Opacity = 2 });
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
