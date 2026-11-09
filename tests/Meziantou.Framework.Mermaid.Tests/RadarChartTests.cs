using System.Xml.Linq;
using Meziantou.Framework.Charts;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class RadarChartTests
{
    [Fact]
    public void RadarChart()
    {
        #region Mermaid_RadarChart
        var diagram = new RadarChart { Title = "Grades", Maximum = 100 };
        diagram.AddAxis("m", "Math");
        diagram.AddAxis("s", "Science");
        diagram.AddAxis("e", "English");
        diagram.AddAxis("h", "History");
        diagram.AddAxis("g", "Geography");
        diagram.AddAxis("a", "Art");
        diagram.AddCurve("a", "Alice", 85, 90, 80, 70, 75, 90);
        diagram.AddCurve("b", "Bob", 70, 75, 85, 80, 90, 85);
        #endregion

        Assert.HasCount(6, diagram.Axes);
        Assert.Equal([85.0, 90, 80, 70, 75, 90], diagram.Curves[0].Values.AsEnumerable());
        ChartAssert.Svg(diagram, "RadarChart");
    }

    [Fact]
    public void PolygonsAndStyle()
    {
        var diagram = new RadarChart
        {
            Graticule = RadarGraticule.Polygon,
            Ticks = 4,
            Minimum = 1,
            Maximum = 5,
            Width = 400,
            Height = 300,
            MarginLeft = 80,
            AxisScaleFactor = 0.5,
            AxisLabelFactor = 1.2,
            AxisColor = SceneColor.Parse("#ff0000"),
            AxisStrokeWidth = 1,
            AxisLabelFontSize = 14,
            CurveOpacity = 0.2,
            CurveStrokeWidth = 3,
            GraticuleColor = SceneColor.Parse("#0000ff"),
            GraticuleStrokeWidth = 2,
            GraticuleOpacity = 0.1,
            LegendFontSize = 16,
            Theme = DiagramTheme.Dark,
        };
        diagram.AddAxis("food", "Food Quality");
        diagram.AddAxis("service", "Service");
        diagram.AddAxis("price", "Price");
        diagram.AddAxis("ambiance");
        diagram.AddCurve("a", "Restaurant A", 4, 3, 2, 4);
        diagram.AddCurve("b", null, 6, 0, 3, 3);

        var svg = diagram.ToSvg();
        Assert.Equal((530.0, 400.0), (diagram.ToPlot().Width, diagram.ToPlot().Height));
        Assert.Contains("stroke=\"#ff0000\"", svg, StringComparison.Ordinal);
        Assert.Contains(">ambiance<", svg, StringComparison.Ordinal);
        Assert.Contains(">b<", svg, StringComparison.Ordinal);
        Assert.Contains("font-size=\"14\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-size=\"16\"", svg, StringComparison.Ordinal);
        Assert.Equal(4, CountPaths(svg, "radarGraticule"));
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void CirclesAndRoundedCurves()
    {
        var diagram = new RadarChart { Ticks = 3 };
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddAxis("d");
        diagram.AddAxis("e");
        diagram.AddCurve("c1", null, 1, 2, 3, 4, 5);
        diagram.AddCurve("c2", null, 5, 4, 3, 2, 1);

        var svg = diagram.ToSvg();

        // One ring per tick, and the curves are smooth
        Assert.Equal(3, CountPaths(svg, "radarGraticule"));
        Assert.All(GetPaths(svg, "radarCurves"), path => Assert.Contains("C", path.Attribute("d")!.Value, StringComparison.Ordinal));
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void CurveTensionOfZeroDrawsStraightLines()
    {
        var diagram = new RadarChart { CurveTension = 0 };
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddCurve("c1", null, 1, 2, 3);

        var path = Assert.Single(GetPaths(diagram.ToSvg(), "radarCurves"));
        Assert.DoesNotContain("C", path.Attribute("d")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void CurvesWithoutAValuePerAxisAreNotDrawn()
    {
        var diagram = new RadarChart { ShowLegend = false, CurveTension = 0 };
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddCurve("drawn", null, 1, 2, 3);
        diagram.AddCurve("missing", null, 1, 2);
        diagram.AddCurve("extra", null, 1, 2, 3, 4);

        var svg = diagram.ToSvg();
        Assert.Equal(1, CountPaths(svg, "radarCurves"));
        Assert.DoesNotContain("class=\"legend\"", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg);
    }

    [Fact]
    public void TheLegendListsEveryCurveWithItsColor()
    {
        var diagram = new RadarChart();
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddCurve("x", "Same", 1, 2, 3);
        diagram.AddCurve("y", "Same", 3, 2, 1);
        diagram.AddCurve("z", "Not drawn", 3);

        var svg = diagram.ToSvg();
        var legend = XDocument.Parse(svg).Descendants().Single(element => element.Attribute("class")?.Value is "legend");
        var swatches = legend.Elements().Where(element => element.Name.LocalName is "rect").Select(element => element.Attribute("fill")!.Value).ToArray();
        Assert.HasCount(3, swatches.Distinct(StringComparer.Ordinal));
        Assert.Equal(["Same", "Same", "Not drawn"], legend.Elements().Where(element => element.Name.LocalName is "text").Select(element => element.Value.Replace("\u200B", "", StringComparison.Ordinal)));
        Assert.Equal(swatches.Take(2), GetPaths(svg, "radarCurves").Select(path => path.Attribute("stroke")!.Value));
    }

    [Fact]
    public void ToPlotThreshold()
    {
        #region Mermaid_RadarChartToPlot
        var chart = new RadarChart { Title = "Player skills", Maximum = 100, Width = 300, Height = 300 };
        chart.AddAxis("speed", "Speed");
        chart.AddAxis("power", "Power");
        chart.AddAxis("agility", "Agility");
        chart.AddAxis("focus", "Focus");
        chart.AddCurve("ada", "Ada", 80, 55, 90, 70);
        chart.AddCurve("bob", "Bob", 60, 85, 50, 75);

        // The radius is the scale of the values: a rule at 50 is a circle halfway to the end of the axes
        var plot = chart.ToPlot();
        plot.Add(new RuleYMark(50) { StrokeColor = SceneColor.Parse("#e5484d"), StrokeWidth = 2, StrokeDashArray = [6, 4] });
        var svg = plot.ToSvg();
        #endregion

        Assert.Contains("stroke=\"#e5484d\"", svg, StringComparison.Ordinal);
        ChartAssert.Svg(svg, "RadarChartToPlot");
    }

    [Fact]
    public void ToPlotUsesTheScaleOfTheChart()
    {
        var diagram = new RadarChart { Minimum = 10, Maximum = 20 };
        diagram.AddAxis("a");
        diagram.AddCurve("c", null, 15);

        var plot = diagram.ToPlot();
        var scale = Assert.IsType<LinearScale>(plot.YScale);
        Assert.Equal((10.0, 20.0), (scale.Minimum, scale.Maximum));
        Assert.IsType<PolarCoordinates>(plot.Coordinates);
        Assert.Equal(new ChartMargin(50), plot.Margin);
    }

    [Fact]
    public void EmptyAndFlatRadars()
    {
        ChartAssert.Svg(new RadarChart());

        // The maximum is the minimum: Mermaid divides by zero, and draws no curve
        var flat = new RadarChart { Ticks = 0 };
        flat.AddAxis("a");
        flat.AddAxis("b");
        flat.AddCurve("c", null, 0, 0);
        var svg = flat.ToSvg();
        AssertFinite(svg);
        Assert.Equal(0, CountPaths(svg, "radarCurves"));
        Assert.Equal(0, CountPaths(svg, "radarGraticule"));
        Assert.Contains("class=\"legend\"", svg, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(RadarGraticule.Circle)]
    [InlineData(RadarGraticule.Polygon)]
    public void EdgeCasesDrawFiniteCoordinates(RadarGraticule graticule)
    {
        foreach (var diagram in CreateEdgeCases())
        {
            diagram.Graticule = graticule;
            var svg = diagram.ToSvg();
            AssertFinite(svg);
        }
    }

    [Fact]
    public void ValuesAreClampedToTheScale()
    {
        var diagram = new RadarChart { Minimum = 2, Maximum = 4, CurveTension = 0, Ticks = 1, Graticule = RadarGraticule.Polygon, ShowLegend = false };
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddAxis("d");
        diagram.AddCurve("out", null, -5, 100, double.NaN, double.PositiveInfinity);
        diagram.AddCurve("in", null, 2, 4, 2, 4);

        // Clamped to the minimum and the maximum, the two curves are the same
        var paths = GetPaths(diagram.ToSvg(), "radarCurves").Select(path => path.Attribute("d")!.Value).ToArray();
        Assert.HasCount(2, paths);
        Assert.Equal(paths[1], paths[0]);
    }

    [Fact]
    public void HugeValuesUseFractionsOfTheRadius()
    {
        var diagram = new RadarChart { AxisLabelFactor = 100, AxisScaleFactor = 100 };
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddCurve("c", null, double.MaxValue, double.MaxValue / 2, 0);

        var svg = diagram.ToSvg();
        AssertFinite(svg);
        Assert.Equal(1, CountPaths(svg, "radarCurves"));
        Assert.Equal((0.0, 1.0), (((LinearScale)diagram.ToPlot().YScale!).Minimum, ((LinearScale)diagram.ToPlot().YScale!).Maximum));
    }

    [Fact]
    public void ParsedEdgeCasesDraw()
    {
        string[] texts =
        [
            "radar-beta",
            "radar-beta\n  axis a",
            "radar-beta\n  axis a, b\n  curve c{1, 2}",
            "radar-beta\n  axis a, b, c\n  curve c{1, 2}\n  curve d{1, 2, 3, 4}",
            "radar-beta\n  axis a, b, c\n  curve c{0, 0, 0}",
            "radar-beta\n  axis a, b, c\n  curve c{5, 6, 7}\n  min 10\n  max 10",
            "radar-beta\n  axis a, b, c\n  curve c{5, 60, 7}\n  min 10\n  max 20\n  graticule polygon\n  ticks 32",
            "radar-beta\n  axis a, b, c\n  curve c{1" + new string('0', 300) + ", 1, 0}\n  ticks 0\n  showLegend false",
            "---\nconfig:\n  radar:\n    axisLabelFactor: 100\n    axisScaleFactor: 100\n    width: 1\n    height: 1\n    marginTop: 0\n---\nradar-beta\n  axis a, b, c\n  curve c{" + "17976931348623157" + new string('0', 292) + ", 1, 2}",
        ];

        foreach (var text in texts)
        {
            var diagram = (RadarChart)MermaidDocument.Parse(text).Content;
            AssertFinite(diagram.ToSvg());
        }
    }

    [Fact]
    public void OptionsAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { Ticks = 33 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { Ticks = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { Maximum = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { Minimum = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { CurveTension = 2 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarChart { CurveOpacity = -0.1 });
        Assert.Throws<ArgumentException>("name", () => new RadarAxis(""));
        Assert.Throws<ArgumentNullException>("name", () => new RadarCurve(null!));
        Assert.Throws<ArgumentNullException>("values", () => new RadarChart().AddCurve("c", null, null!));
    }

    private static IEnumerable<RadarChart> CreateEdgeCases()
    {
        // No axis, one axis, two axes
        for (var axisCount = 0; axisCount <= 2; axisCount++)
        {
            var chart = new RadarChart();
            for (var i = 0; i < axisCount; i++)
            {
                chart.AddAxis("a" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            chart.AddCurve("c", null, Enumerable.Repeat(1.0, axisCount));
            yield return chart;
        }

        // No curve
        var empty = new RadarChart();
        empty.AddAxis("a");
        empty.AddAxis("b");
        empty.AddAxis("c");
        yield return empty;

        // Fewer and more values than axes, the minimum is the maximum, values out of the scale, negative, huge and missing values
        double[][] curves =
        [
            [1],
            [1, 2, 3, 4],
            [0, 0, 0],
            [-1, -2, -3],
            [double.MaxValue, 1, 0],
            [double.MaxValue, double.MaxValue, double.MaxValue],
            [double.NaN, 1, 2],
            [double.PositiveInfinity, double.NegativeInfinity, 1],
            [double.Epsilon, 0, double.Epsilon],
        ];
        foreach (var values in curves)
        {
            foreach (var (minimum, maximum) in new (double, double?)[] { (0, null), (5, 5), (5, 1), (0, double.MaxValue), (double.MaxValue, null), (1e-300, 2e-300) })
            {
                var chart = new RadarChart { Minimum = minimum, Maximum = maximum, AxisLabelFactor = 100, AxisScaleFactor = 0 };
                chart.AddAxis("a", "Line 1\nLine 2");
                chart.AddAxis("b", "");
                chart.AddAxis("c");
                chart.AddCurve("c", null, values);
                chart.AddCurve("d", "", values);
                yield return chart;
            }
        }

        // The smallest and the largest sizes
        var small = new RadarChart { Width = 1, Height = 1, MarginTop = 0, MarginRight = 0, MarginBottom = 0, MarginLeft = 0, Ticks = 32 };
        small.AddAxis("a");
        small.AddAxis("b");
        small.AddAxis("c");
        small.AddCurve("c", null, 1, 2, 3);
        yield return small;

        var large = new RadarChart { Width = 10_000, Height = 10_000, MarginTop = 10_000, MarginRight = 10_000, MarginBottom = 10_000, MarginLeft = 10_000, AxisLabelFontSize = 200, LegendFontSize = 200 };
        large.AddAxis("a");
        large.AddAxis("b");
        large.AddAxis("c");
        large.AddCurve("c", null, 1, 2, 3);
        yield return large;
    }

    private static void AssertFinite(string svg)
    {
        Assert.Equal("svg", XDocument.Parse(svg).Root!.Name.LocalName);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("∞", svg, StringComparison.Ordinal);
    }

    private static int CountPaths(string svg, string cssClass) => GetPaths(svg, cssClass).Count();

    private static IEnumerable<XElement> GetPaths(string svg, string cssClass)
    {
        return XDocument.Parse(svg).Descendants()
            .Where(element => element.Attribute("class")?.Value == cssClass)
            .SelectMany(element => element.Descendants())
            .Where(element => element.Name.LocalName is "path" && !string.IsNullOrEmpty(element.Attribute("d")?.Value));
    }
}
