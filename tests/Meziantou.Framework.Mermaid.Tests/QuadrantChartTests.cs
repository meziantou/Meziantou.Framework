using System.Globalization;
using System.Xml.Linq;
using Meziantou.Framework.Charts;
using Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class QuadrantChartTests
{
    [Fact]
    public void QuadrantChart()
    {
        #region Mermaid_QuadrantChart
        var diagram = new QuadrantChart
        {
            Title = "Reach and engagement of campaigns",
            XAxisLeftLabel = "Low Reach",
            XAxisRightLabel = "High Reach",
            YAxisBottomLabel = "Low Engagement",
            YAxisTopLabel = "High Engagement",
            Quadrant1Label = "We should expand",
            Quadrant2Label = "Need to promote",
            Quadrant3Label = "Re-evaluate",
            Quadrant4Label = "May be improved",
        };
        diagram.AddPoint("Campaign A", 0.3, 0.6);
        diagram.AddPoint("Campaign B", 0.45, 0.23);
        diagram.AddPoint("Campaign C", 0.57, 0.69).Style.Color = SceneColor.Parse("#ff3300");
        diagram.AddPoint("Campaign D", 0.78, 0.34).ClassName = "important";
        diagram.ClassDefinitions["important"] = new QuadrantChartPointStyle { Radius = 10, Color = SceneColor.Parse("#109060") };
        #endregion

        Assert.HasCount(4, diagram.Points);
        Assert.Equal("Campaign D", diagram.Points[3].ToString());
        ChartAssert.Svg(diagram, "QuadrantChart");
    }

    [Fact]
    public void WithoutPoints()
    {
        // The texts of the quadrants are in their middle, and the x axis is at the top by default
        var diagram = new QuadrantChart
        {
            XAxisLeftLabel = "Urgent",
            XAxisRightLabel = "Not Urgent",
            YAxisBottomLabel = "Not Important",
            YAxisTopLabel = "Important",
            Quadrant1Label = "Plan",
            Quadrant2Label = "Do",
            Quadrant3Label = "Delegate",
            Quadrant4Label = "Delete",
            Width = 400,
            Height = 400,
        };
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void AxisPositionsBordersAndColors()
    {
        var diagram = new QuadrantChart
        {
            XAxisLeftLabel = "Cheap",
            XAxisRightLabel = "Expensive",
            YAxisBottomLabel = "Slow",
            YAxisTopLabel = "Fast",
            Quadrant1Label = "Line 1\nLine 2",
            XAxisPosition = QuadrantChartXAxisPosition.Bottom,
            YAxisPosition = QuadrantChartYAxisPosition.Right,
            InternalBorderWidth = 0,
            ExternalBorderWidth = 6,
            QuadrantPadding = 20,
            QuadrantLabelFontSize = 24,
            XAxisLabelFontSize = 12,
            YAxisLabelFontSize = 20,
            Width = 600,
            Height = 300,
        };
        diagram.Colors.Quadrant2Fill = SceneColor.Parse("#ffe0e0");
        diagram.Colors.Quadrant4TextFill = SceneColor.Parse("#800000");
        diagram.Colors.XAxisTextFill = SceneColor.Parse("#008000");
        diagram.Colors.YAxisTextFill = SceneColor.Parse("#000080");
        diagram.Colors.ExternalBorderStrokeFill = SceneColor.Parse("#ff8800");
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void SingleAxisTextsStylesAndDarkTheme()
    {
        var diagram = new QuadrantChart
        {
            Theme = DiagramTheme.Dark,
            XAxisLeftLabel = "Effort ⟶",
            YAxisBottomLabel = "Value",
            YAxisPosition = QuadrantChartYAxisPosition.Right,
            Quadrant1Label = DiagramText.Markdown("**Quick wins**"),
        };
        diagram.Colors.Quadrant1Fill = SceneColor.Parse("#336633");
        diagram.Colors.PointTextFill = SceneColor.Parse("#ffcc00");
        var point = diagram.AddPoint("Bordered", 0, 1);
        point.Style.Radius = 25;
        point.Style.StrokeColor = SceneColor.Parse("#10f0f0");
        point.Style.StrokeWidth = 5;
        var huge = diagram.AddPoint("Huge radius", 1, 0);
        huge.Style.Radius = 400;
        huge.Style.Color = SceneColor.Parse("#446688");
        diagram.AddPoint(new DiagramText("*markdown*", DiagramTextFormat.Markdown), 0.5, 0.5).ClassName = "missing";
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void EmptyAndZeroSizedDiagrams()
    {
        ChartAssert.Svg(new QuadrantChart());
        var zero = new QuadrantChart { Width = 0, Height = 0, XAxisLeftLabel = "x", YAxisBottomLabel = "y", Title = "Title" };
        zero.AddPoint("p", 0.5, 0.5);
        var scene = zero.CreateScene();
        Assert.True(double.IsFinite(scene.Width) && scene.Width > 0);
        AssertFinite(zero);
    }

    [Fact]
    public void EdgeCasesDrawWithFiniteCoordinates()
    {
        // Points at the corners, empty and multi-line texts, a missing class, no border
        var corners = new QuadrantChart { Quadrant1Label = "", XAxisRightLabel = "right only", YAxisTopLabel = "top only", InternalBorderWidth = 0, ExternalBorderWidth = 0 };
        corners.AddPoint("", 0, 0);
        corners.AddPoint("\n", 1, 1).ClassName = "missing";
        corners.AddPoint("two\nlines", 0, 1).Style.Radius = 0;
        corners.AddPoint(DiagramText.Markdown(""), 1, 0);
        AssertFinite(corners);

        // The largest values the model accepts
        var largest = new QuadrantChart
        {
            Title = "Largest",
            Width = 10_000,
            Height = 10_000,
            QuadrantPadding = 10_000,
            QuadrantTextTopPadding = 10_000,
            QuadrantLabelFontSize = 1_000,
            InternalBorderWidth = 1_000,
            ExternalBorderWidth = 1_000,
            XAxisLabelPadding = 10_000,
            XAxisLabelFontSize = 1_000,
            YAxisLabelPadding = 10_000,
            YAxisLabelFontSize = 1_000,
            PointTextPadding = 10_000,
            PointLabelFontSize = 1_000,
            PointRadius = 10_000,
            XAxisLeftLabel = "left",
            YAxisBottomLabel = "bottom",
            Quadrant3Label = "q3",
        };
        largest.AddPoint("p", 0.5, 0.5).Style.StrokeWidth = 1_000_000;
        largest.ClassDefinitions["c"] = new QuadrantChartPointStyle { Radius = 1_000_000 };
        largest.AddPoint("q", 1, 1).ClassName = "c";
        AssertFinite(largest);

        // Many points
        var many = new QuadrantChart();
        for (var i = 0; i < 500; i++)
        {
            many.AddPoint("Point " + i.ToString(CultureInfo.InvariantCulture), (i % 25) / 24.0, (i / 25) / 19.0);
        }

        var svg = AssertFinite(many);
        Assert.HasCount(500, XDocument.Parse(svg).Descendants().Where(element => element.Name.LocalName is "circle").ToList());
    }

    [Fact]
    public void ToPlotDrawsOnFixedScales()
    {
        var diagram = new QuadrantChart { Width = 300, Height = 200, XAxisLeftLabel = "left", YAxisBottomLabel = "bottom" };
        diagram.AddPoint("A", 0.25, 0.75);
        var plot = diagram.ToPlot();
        Assert.Equal(300, plot.Width);
        Assert.Equal(200, plot.Height);
        var xScale = Assert.IsType<LinearScale>(plot.XScale);
        Assert.Equal(0, xScale.Minimum);
        Assert.Equal(1, xScale.Maximum);
        var yScale = Assert.IsType<LinearScale>(plot.YScale);
        Assert.Equal(0, yScale.Minimum);
        Assert.Equal(1, yScale.Maximum);
        Assert.False(plot.XAxis.Visible);
        Assert.False(plot.YAxis.Visible);

        // The y axis is on the left and the x axis at the bottom: padding + 2 * label padding + font size = 31
        Assert.Equal(new ChartMargin(5, 5, 31, 31), plot.Margin);
        Assert.Single(plot.Marks.OfType<DotMark<QuadrantChartPoint>>());

        // The title moves the chart down, and makes it taller
        diagram.Title = "Title";
        var withTitle = diagram.ToPlot();
        Assert.True(withTitle.Height > 200);
        Assert.Equal(withTitle.Height - 200 + 5, withTitle.Margin!.Value.Top, 6);
    }

    [Fact]
    public void ToPlotCustomizesTheDrawing()
    {
        #region Mermaid_QuadrantChartToPlot
        var diagram = new QuadrantChart { Title = "Risks", XAxisLeftLabel = "Unlikely", XAxisRightLabel = "Likely" };
        diagram.AddPoint("Outage", 0.8, 0.9);
        diagram.AddPoint("Data loss", 0.2, 0.95);

        // The axes go from 0 to 1: a dashed line marks the likelihood above which a risk needs a plan
        var plot = diagram.ToPlot();
        plot.Add(new RuleXMark(0.7) { StrokeColor = SceneColor.Parse("#cc0000"), StrokeDashArray = [4, 4] });
        var svg = plot.ToSvg();
        #endregion

        ChartAssert.Svg(svg);
    }

    [Fact]
    public void ParsedChartDrawsTheConfigurationAndTheThemeVariables()
    {
        var diagram = (QuadrantChart)MermaidDocument.Parse("""
            ---
            config:
              quadrantChart:
                chartWidth: 400
                chartHeight: 300
                xAxisPosition: bottom
                yAxisPosition: right
              themeVariables:
                quadrant1Fill: "#ff0000"
                quadrantPointFill: "#00ff00"
            ---
            quadrantChart
              x-axis Low --> High
              y-axis Bad --> Good
              quadrant-1 Best
              A: [0.9, 0.9] radius: 12, stroke-color: #0000ff, stroke-width: 3px
            """).Content;
        var svg = diagram.ToSvg();
        Assert.Contains("#ff0000", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#00ff00", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#0000ff", svg, StringComparison.OrdinalIgnoreCase);
        ChartAssert.Svg(diagram);
    }

    [Fact]
    public void InvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>("x", () => new QuadrantChartPoint("p", 1.5, 0));
        Assert.Throws<ArgumentOutOfRangeException>("y", () => new QuadrantChartPoint("p", 0, double.NaN));
        Assert.Throws<ArgumentNullException>("label", () => new QuadrantChartPoint(null!, 0, 0));
        var point = new QuadrantChartPoint("p", 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => point.X = -0.1);
        Assert.Throws<ArgumentNullException>("value", () => point.Label = null!);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => point.Style.Radius = -1);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => point.Style.StrokeWidth = 2_000_000);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new QuadrantChart { Width = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new QuadrantChart { PointLabelFontSize = double.PositiveInfinity });
    }

    [Fact]
    public void AdjustAddsToTheChannels()
    {
        Assert.Equal(SceneColor.Parse("#f1f1ff"), QuadrantChartPlotBuilder.Adjust(SceneColor.Parse("#ECECFF"), 5));
        Assert.Equal(SceneColor.Parse("#0e0e00"), QuadrantChartPlotBuilder.Adjust(SceneColor.Parse("#131300"), -5));
    }

    [Fact]
    public void ColorsDeriveFromTheTheme()
    {
        var theme = DiagramTheme.Default;
        var colors = QuadrantChartPlotBuilder.Resolve(new QuadrantChartColors(), theme);
        Assert.Equal(theme.PrimaryColor, colors.Quadrant1Fill);
        Assert.Equal(QuadrantChartPlotBuilder.Adjust(theme.PrimaryColor, 15), colors.Quadrant4Fill);
        Assert.Equal(QuadrantChartPlotBuilder.Adjust(theme.PrimaryTextColor, -10), colors.Quadrant3TextFill);
        Assert.Equal(SceneColor.Black, colors.PointFill);
        Assert.Equal(theme.PrimaryBorderColor, colors.ExternalBorderStrokeFill);
        Assert.Equal(DiagramTheme.Dark.PrimaryTextColor, QuadrantChartPlotBuilder.Resolve(new QuadrantChartColors(), DiagramTheme.Dark).PointFill);

        var overridden = QuadrantChartPlotBuilder.Resolve(new QuadrantChartColors { Quadrant2Fill = SceneColor.White, PointTextFill = SceneColor.Black }, theme);
        Assert.Equal(SceneColor.White, overridden.Quadrant2Fill);
        Assert.Equal(SceneColor.Black, overridden.PointTextFill);
    }

    [Theory]
    [InlineData("**bold** and *italic*", "bold and italic")]
    [InlineData("snake_case __word__", "snake_case word")]
    [InlineData(@"\*escaped\*", "*escaped*")]
    public void MarkdownEmphasisIsNotDrawn(string markdown, string expected)
    {
        Assert.Equal(expected, QuadrantChartPlotBuilder.ToPlainText(DiagramText.Markdown(markdown)));
        Assert.Equal(markdown, QuadrantChartPlotBuilder.ToPlainText(markdown));
        Assert.Equal("", QuadrantChartPlotBuilder.ToPlainText(null));
    }

    private static string AssertFinite(QuadrantChart chart)
    {
        var svg = chart.ToSvg();
        Assert.Equal("svg", XDocument.Parse(svg).Root!.Name.LocalName);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("∞", svg, StringComparison.Ordinal);
        return svg;
    }
}
