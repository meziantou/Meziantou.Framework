using System.Xml.Linq;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;
using Meziantou.Framework.Mermaid.Internals.Charts;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class XyChartTests
{
    [Fact]
    public void XyChart()
    {
        #region Mermaid_XyChart
        var diagram = new XyChart { Title = "Sales revenue" };
        foreach (var month in new[] { "Jan", "Feb", "Mar", "Apr", "May", "Jun" })
        {
            diagram.XAxis.Categories.Add(month);
        }

        diagram.YAxis.Title = "Revenue (k$)";
        diagram.YAxis.Range = new XyChartAxisRange(0, 120);
        diagram.AddBars([50, 62, 75, 82, 95, 105], "Actual");
        diagram.AddLine([55, 60, 70, 85, 90, 110], "Target");
        #endregion

        Assert.HasCount(2, diagram.Series);
        ChartAssert.Svg(diagram, "XyChart");
    }

    [Fact]
    public void ToPlot()
    {
        #region Mermaid_XyChartToPlot
        var chart = (XyChart)MermaidDocument.Parse("""
            xychart
                title "Sales Revenue"
                x-axis [jan, feb, mar, apr, may, jun]
                bar [5000, 6000, 7500, 8200, 9500, 10500]
            """).Content;

        var plot = chart.ToPlot();
        plot.Subtitle = "First half of the year";
        plot.YAxis.Grid = false;
        var svg = plot.ToSvg();
        #endregion

        Assert.Contains("First half of the year", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void HorizontalWithDataLabels()
    {
        var diagram = new XyChart { Title = "Horizontal", Orientation = XyChartOrientation.Horizontal, ShowDataLabels = true, Theme = DiagramTheme.Forest };
        diagram.XAxis.Title = "Quarter";
        foreach (var quarter in new[] { "Q1", "Q2", "Q3" })
        {
            diagram.XAxis.Categories.Add(quarter);
        }

        diagram.AddBars([12, 30, 21]);
        diagram.AddLine([10, 25, 28]).PointLabels.Add("Start");
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void DataLabelsOutsideBarsAndColors()
    {
        var diagram = new XyChart
        {
            ShowDataLabels = true,
            DataLabelsOutsideBars = true,
            Width = 400,
            Height = 300,
            BackgroundColor = SceneColor.Parse("#fafafa"),
            DataLabelColor = SceneColor.Parse("#c00000"),
        };
        diagram.PlotColors.Add(SceneColor.Parse("#4e79a7"));
        diagram.XAxis.Categories.Add("a");
        diagram.XAxis.Categories.Add("b");
        diagram.XAxis.LineColor = SceneColor.Parse("#ff0000");
        diagram.YAxis.ShowTicks = false;
        diagram.YAxis.Range = new XyChartAxisRange(0, 10);
        diagram.AddBars([3, 7]);
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void NumbersOnTheXAxisAndRotatedLabels()
    {
        var diagram = new XyChart { Theme = DiagramTheme.Dark, ShowLegend = false };
        diagram.XAxis.LabelRotation = -45;
        diagram.XAxis.Title = "Day";
        diagram.AddLine([1.3, 0.6, 2.4, -0.34, 1], "Hidden from the legend");
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void NegativeBarsOnANumberAxis()
    {
        var diagram = (XyChart)MermaidDocument.Parse("""
            xychart
                title "Balance"
                x-axis "Week" 10 --> 50
                y-axis "Amount"
                bar Balance [-3, 5, -1.5, 8, 2]
                line Trend [-2, 1, 2, 4, 6]
            """).Content;
        diagram.ShowDataLabels = true;
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void ParsedChartIsDrawn()
    {
        var plot = ((XyChart)MermaidDocument.Parse("""
            ---
            config:
              xyChart:
                width: 900
                height: 600
                showTitle: false
                titleFontSize: 30
                xAxis:
                  labelFontSize: 18
            ---
            %%{init: {"themeVariables": {"xyChart": {"plotColorPalette": "#ff0000, #00ff00", "titleColor": "#123456", "yAxisLabelColor": "#654321", "yAxisLineColor": "#abcdef"}}}}%%
            xychart horizontal
                title "Hidden"
                x-axis [a, b]
                y-axis 10 --> 0
                bar first [1, 2]
                line second [2, 1]
            """).Content).ToPlot();

        Assert.Equal((900.0, 600.0), (plot.Width, plot.Height));
        Assert.Null(plot.Title);
        Assert.True(Assert.IsType<CartesianCoordinates>(plot.Coordinates).Transposed);
        Assert.Equal((30.0, 18.0), (plot.Theme.TitleFontSize, plot.Theme.TickFontSize));
        Assert.Equal((SceneColor.Parse("#123456"), SceneColor.Parse("#654321"), SceneColor.Parse("#abcdef")), (plot.Theme.Foreground, plot.Theme.MutedForeground, plot.Theme.AxisColor));

        var yScale = Assert.IsType<LinearScale>(plot.YScale);
        Assert.Equal((0.0, 10.0, true), (yScale.Minimum, yScale.Maximum, yScale.Reverse));
        Assert.Equal(["a", "b"], Assert.IsType<BandScale>(plot.XScale).Domain);

        var colors = Assert.IsType<OrdinalColorScale>(plot.ColorScale);
        Assert.Equal(["first", "second"], colors.Domain);
        Assert.Equal([SceneColor.Parse("#ff0000"), SceneColor.Parse("#00ff00")], colors.Palette);
        Assert.Equal((true, LegendPosition.Right), (plot.Legend.Visible, plot.Legend.Position));
    }

    [Fact]
    public void PaletteOfTheTheme()
    {
        static SceneColor ColorOf(DiagramTheme theme)
        {
            var chart = new XyChart { Theme = theme };
            chart.AddBars([1]);
            return Assert.IsType<BarMark<XyChartDatum>>(chart.ToPlot().Marks[0]).FillColor!.Value;
        }

        Assert.Equal(SceneColor.Parse("#ECECFF"), ColorOf(DiagramTheme.Default));
        Assert.Equal(SceneColor.Parse("#3498db"), ColorOf(DiagramTheme.Dark));
        Assert.Equal(SceneColor.Parse("#CDE498"), ColorOf(DiagramTheme.Forest));
        Assert.Equal(SceneColor.Parse("#EEE"), ColorOf(DiagramTheme.Neutral));
        Assert.Equal(SceneColor.Parse("#FFF4DD"), ColorOf(DiagramTheme.Base));
        Assert.Equal(SceneColor.Parse("#3498db"), ColorOf(DiagramTheme.Default with { IsDark = true }));
    }

    [Fact]
    public void SeriesTakeTheColorsInTurn()
    {
        var chart = new XyChart();
        chart.PlotColors.Add(SceneColor.Parse("#111111"));
        chart.PlotColors.Add(SceneColor.Parse("#222222"));
        chart.AddBars([1, 2]);
        chart.AddLine([1, 2]);
        chart.AddLine([2, 3], "named");
        chart.AddBars([3, 4], "named");

        var plot = chart.ToPlot();
        Assert.Equal(SceneColor.Parse("#111111"), Assert.IsType<BarMark<XyChartDatum>>(plot.Marks[0]).FillColor);
        Assert.Equal(SceneColor.Parse("#222222"), Assert.IsType<LineMark<XyChartDatum>>(plot.Marks[1]).StrokeColor);

        // The legend lists a name once: the next series of the same name keeps its own color, out of the legend
        Assert.NotNull(Assert.IsType<LineMark<XyChartDatum>>(plot.Marks[2]).Stroke);
        Assert.Equal(SceneColor.Parse("#222222"), Assert.IsType<BarMark<XyChartDatum>>(plot.Marks[3]).FillColor);
        Assert.Equal(["named"], Assert.IsType<OrdinalColorScale>(plot.ColorScale).Domain);
        Assert.Equal([SceneColor.Parse("#111111")], Assert.IsType<OrdinalColorScale>(plot.ColorScale).Palette);

        chart.ShowLegend = false;
        plot = chart.ToPlot();
        Assert.Null(plot.ColorScale);
        Assert.False(plot.Legend.Visible);
    }

    [Fact]
    public void ValuesBelongToTheCategoriesAtTheirIndex()
    {
        var chart = new XyChart();
        chart.XAxis.Categories.Add("a");
        chart.XAxis.Categories.Add("b");
        chart.XAxis.Categories.Add("a");
        chart.AddBars([1, 2, 3, 4, 5]);
        chart.AddLine([1]);

        var plot = chart.ToPlot();
        Assert.Equal(["a", "b"], Assert.IsType<BandScale>(plot.XScale).Domain);
        Assert.Equal(["a", "b", "a"], Assert.IsType<BarMark<XyChartDatum>>(plot.Marks[0]).Data.Select(row => row.X));
        Assert.Equal(["a"], Assert.IsType<LineMark<XyChartDatum>>(plot.Marks[1]).Data.Select(row => row.X));
        AssertValidSvg(chart);
    }

    [Fact]
    public void ValuesSpreadOverTheNumberAxis()
    {
        var chart = new XyChart();
        chart.XAxis.Range = new XyChartAxisRange(100, 0);
        chart.AddLine([1, 2, 3, 4, 5]);
        chart.AddLine([1, 2, 3]);
        chart.AddLine([7]);

        var plot = chart.ToPlot();
        var scale = Assert.IsType<LinearScale>(plot.XScale);
        Assert.Equal((0.0, 100.0, true), (scale.Minimum, scale.Maximum, scale.Reverse));
        Assert.Equal([100.0, 75, 50, 25, 0], Assert.IsType<LineMark<XyChartDatum>>(plot.Marks[0]).Data.Select(row => (double)row.X));
        Assert.Equal([100.0, 50, 0], Assert.IsType<LineMark<XyChartDatum>>(plot.Marks[1]).Data.Select(row => (double)row.X));
        Assert.Equal([100.0], Assert.IsType<LineMark<XyChartDatum>>(plot.Marks[2]).Data.Select(row => (double)row.X));

        // Without range, the values of the first series are numbered from 1, and the bars at the ends stay in the plot
        chart = new XyChart();
        chart.AddBars([1, 2, 3]);
        scale = Assert.IsType<LinearScale>(chart.ToPlot().XScale);
        Assert.Equal((0.5, 3.5), (scale.Minimum, scale.Maximum));
        AssertValidSvg(chart);
    }

    [Fact]
    public void ValuesOutOfTheRangeAreClipped()
    {
        var chart = new XyChart();
        chart.YAxis.Range = new XyChartAxisRange(0, 10);
        chart.AddBars([5, 8]);
        Assert.False(chart.ToPlot().Clip);

        chart.AddLine([5, 20]);
        Assert.True(chart.ToPlot().Clip);
        AssertValidSvg(chart);
    }

    [Fact]
    public void AxesCanBeHidden()
    {
        var chart = new XyChart();
        chart.XAxis.Title = "x";
        chart.XAxis.ShowTitle = false;
        chart.XAxis.ShowLabels = false;
        chart.XAxis.ShowTicks = false;
        chart.XAxis.ShowLine = false;
        chart.YAxis.ShowLabels = false;
        chart.YAxis.TickLength = 8;
        chart.AddLine([1, 2, 3]);

        var plot = chart.ToPlot();
        Assert.Equal((false, (string?)null), (plot.XAxis.Visible, plot.XAxis.Label));
        Assert.Equal((null, 8.0, true), (plot.YAxis.Visible, plot.YAxis.TickSize, plot.YAxis.Line));
        Assert.Equal(string.Empty, Assert.IsType<LinearScale>(plot.YScale).TickFormat!(1));
        AssertValidSvg(chart);
    }

    [Fact]
    public void EdgeCasesDrawNothingInvalid()
    {
        // No series, no axis
        AssertValidSvg(new XyChart());

        // Axes without series
        var chart = new XyChart { Title = "Empty" };
        chart.XAxis.Categories.Add("a");
        chart.YAxis.Range = new XyChartAxisRange(0, 10);
        AssertValidSvg(chart);

        chart = new XyChart();
        chart.XAxis.Range = new XyChartAxisRange(1, 5);
        chart.YAxis.Title = "Only a title";
        AssertValidSvg(chart);

        // Empty series, a series of a single value, and empty labels
        chart = new XyChart { ShowDataLabels = true };
        chart.AddBars([]);
        chart.AddLine([]);
        chart.AddBars([4], string.Empty);
        var line = chart.AddLine([3], string.Empty);
        line.PointLabels.Add(string.Empty);
        line.PointLabels.Add("past the last value");
        AssertValidSvg(chart);

        // Empty categories, more categories than values
        chart = new XyChart { ShowDataLabels = true, DataLabelsOutsideBars = true };
        chart.XAxis.Categories.Add(string.Empty);
        chart.XAxis.Categories.Add(string.Empty);
        chart.XAxis.Categories.Add("c");
        chart.AddBars([1]);
        chart.AddLine([1, 2]);
        AssertValidSvg(chart);

        // Ranges of a single value
        chart = new XyChart { ShowDataLabels = true };
        chart.XAxis.Range = new XyChartAxisRange(5, 5);
        chart.YAxis.Range = new XyChartAxisRange(3, 3);
        chart.AddBars([3, 3]);
        chart.AddLine([3, 3, 3]);
        AssertValidSvg(chart);

        chart = new XyChart();
        chart.XAxis.Range = new XyChartAxisRange(0, 0);
        chart.YAxis.Range = new XyChartAxisRange(0, 0);
        chart.AddBars([0]);
        AssertValidSvg(chart);

        // Values that are all the same
        chart = new XyChart();
        chart.AddBars([0, 0, 0]);
        chart.AddLine([0, 0, 0]);
        AssertValidSvg(chart);

        // Huge and negative values
        chart = new XyChart { ShowDataLabels = true, Orientation = XyChartOrientation.Horizontal };
        chart.AddBars([1e300, -1e300, double.MaxValue, double.MinValue], "Huge");
        chart.AddLine([-1e-300, 1e-300, double.Epsilon, 0], "Tiny");
        AssertValidSvg(chart);

        chart = new XyChart { ShowDataLabels = true };
        chart.XAxis.Range = new XyChartAxisRange(double.MinValue, double.MaxValue);
        chart.YAxis.Range = new XyChartAxisRange(double.MaxValue, double.MinValue);
        chart.AddBars([1, 2, 3]);
        chart.AddLine([double.MaxValue, double.MinValue, 0]);
        AssertValidSvg(chart);

        chart = new XyChart();
        chart.XAxis.Range = new XyChartAxisRange(-1e300, 1e300);
        chart.AddBars([-5, -6]);
        AssertValidSvg(chart);

        // Values and ranges that are not finite, which only the API can make
        chart = new XyChart { ShowDataLabels = true };
        chart.XAxis.Categories.Add("only");
        chart.AddBars([double.NaN, 4]);
        chart.AddLine([5, double.PositiveInfinity, 6]);
        chart.AddBars([double.NegativeInfinity]);
        AssertValidSvg(chart);

        chart = new XyChart();
        chart.YAxis.Range = new XyChartAxisRange(double.NaN, double.MaxValue);
        chart.XAxis.Range = new XyChartAxisRange(double.NegativeInfinity, 1);
        chart.AddLine([1, 1, 1]);
        AssertValidSvg(chart);
    }

    [Fact]
    public void TitleIsDrawnInsideTheChart()
    {
        var diagram = new XyChart { Title = "Inside", Width = 300, Height = 200 };
        diagram.AddBars([1, 2]);
        var scene = diagram.CreateScene();
        Assert.Equal((300.0, 200.0), (scene.Width, scene.Height));
        Assert.Contains(">Inside</text>", diagram.ToSvg(), StringComparison.Ordinal);

        diagram.ShowTitle = false;
        Assert.DoesNotContain("Inside</text>", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void PropertiesAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChart { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChart { Height = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChart { PlotReservedSpacePercent = 101 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChart().XAxis.LabelRotation = 91);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChart().YAxis.LabelFontSize = 0);
        Assert.Throws<ArgumentNullException>("values", () => new XyChart().AddBars(null!));
    }

    [Theory]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(1e21, "1e+21")]
    [InlineData(123456789012345680000.0, "123456789012345680000")]
    [InlineData(1e-7, "1e-7")]
    [InlineData(0.000001, "0.000001")]
    [InlineData(-1.5e-10, "-1.5e-10")]
    [InlineData(-0.0, "0")]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.NegativeInfinity, "-Infinity")]
    public void NumbersAreWrittenAsJavaScript(double value, string expected)
    {
        Assert.Equal(expected, JavaScriptNumber.ToString(value));
    }

    private static void AssertValidSvg(XyChart chart)
    {
        var svg = chart.ToSvg();
        Assert.Equal("svg", XDocument.Parse(svg).Root!.Name.LocalName);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
    }
}
