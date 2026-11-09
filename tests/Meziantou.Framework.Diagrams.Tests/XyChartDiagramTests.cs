using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class XyChartDiagramTests
{
    [Fact]
    public void XyChartDiagram()
    {
        #region Diagrams_XyChartDiagram
        var diagram = new XyChartDiagram { Title = "Sales revenue" };
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
        DiagramAssert.Svg(diagram, "XyChartDiagram");
    }

    [Fact]
    public void HorizontalWithDataLabels()
    {
        var diagram = new XyChartDiagram { Title = "Horizontal", Orientation = XyChartOrientation.Horizontal, ShowDataLabels = true, Theme = DiagramTheme.Forest };
        diagram.XAxis.Title = "Quarter";
        foreach (var quarter in new[] { "Q1", "Q2", "Q3" })
        {
            diagram.XAxis.Categories.Add(quarter);
        }

        diagram.AddBars([12, 30, 21]);
        diagram.AddLine([10, 25, 28]).PointLabels.Add("Start");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void DataLabelsOutsideBarsAndColors()
    {
        var diagram = new XyChartDiagram
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
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void NumbersOnTheXAxisAndRotatedLabels()
    {
        var diagram = new XyChartDiagram { Theme = DiagramTheme.Dark, ShowLegend = false };
        diagram.XAxis.LabelRotation = -45;
        diagram.XAxis.Title = "Day";
        diagram.AddLine([1.3, 0.6, 2.4, -0.34, 1], "Hidden from the legend");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void DegenerateValuesDrawNothingInvalid()
    {
        var diagram = new XyChartDiagram { Title = "Degenerate" };
        diagram.XAxis.Categories.Add("only");
        diagram.AddBars([double.NaN, 4]);
        diagram.AddLine([5, double.PositiveInfinity, 6]);
        diagram.AddBars([]);
        var svg = diagram.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);

        var empty = new XyChartDiagram().ToSvg();
        Assert.StartsWith("<svg", empty, StringComparison.Ordinal);

        var flat = new XyChartDiagram();
        flat.YAxis.Range = new XyChartAxisRange(double.NaN, double.MaxValue);
        flat.XAxis.Range = new XyChartAxisRange(double.MinValue, double.MaxValue);
        flat.AddLine([1, 1, 1]);
        Assert.DoesNotContain("NaN", flat.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void TitleIsDrawnInsideTheChart()
    {
        var diagram = new XyChartDiagram { Title = "Inside", Width = 300, Height = 200 };
        diagram.AddBars([1, 2]);
        var scene = diagram.CreateScene();
        Assert.Equal((300 + (2 * diagram.Padding), 200 + (2 * diagram.Padding)), (scene.Width, scene.Height));

        diagram.ShowTitle = false;
        Assert.DoesNotContain("Inside</text>", diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void PropertiesAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChartDiagram { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChartDiagram { Height = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChartDiagram { PlotReservedSpacePercent = 101 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChartDiagram().XAxis.LabelRotation = 91);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new XyChartDiagram().YAxis.LabelFontSize = 0);
        Assert.Throws<ArgumentNullException>("values", () => new XyChartDiagram().AddBars(null!));
    }

    [Theory]
    [InlineData(4000, 11000, "4000,4500,5000,5500,6000,6500,7000,7500,8000,8500,9000,9500,10000,10500,11000")]
    [InlineData(0, 198.2, "0,20,40,60,80,100,120,140,160,180")]
    [InlineData(-0.34, 2.4, "-0.2,0,0.2,0.4,0.6,0.8,1,1.2,1.4,1.6,1.8,2,2.2,2.4")]
    [InlineData(1, 1, "1")]
    [InlineData(10, 0, "10,9,8,7,6,5,4,3,2,1,0")]
    public void TicksAreThoseOfD3(double start, double stop, string expected)
    {
        var ticks = XyChartNumbers.Ticks(start, stop, 10);
        Assert.Equal(expected, string.Join(',', ticks.Select(XyChartNumbers.ToJavaScriptString)));
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
        Assert.Equal(expected, XyChartNumbers.ToJavaScriptString(value));
    }
}
