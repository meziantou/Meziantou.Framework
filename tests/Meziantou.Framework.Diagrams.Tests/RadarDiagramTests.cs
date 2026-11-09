namespace Meziantou.Framework.Diagrams.Tests;

public sealed class RadarDiagramTests
{
    [Fact]
    public void RadarDiagram()
    {
        #region Diagrams_RadarDiagram
        var diagram = new RadarDiagram { Title = "Grades", Maximum = 100 };
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
        DiagramAssert.Svg(diagram, "RadarDiagram");
    }

    [Fact]
    public void PolygonsAndStyle()
    {
        var diagram = new RadarDiagram
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
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void CurvesWithoutAValuePerAxisAreNotDrawn()
    {
        var diagram = new RadarDiagram { ShowLegend = false, CurveTension = 0 };
        diagram.AddAxis("a");
        diagram.AddAxis("b");
        diagram.AddAxis("c");
        diagram.AddCurve("drawn", null, 1, 2, 3);
        diagram.AddCurve("missing", null, 1, 2);

        var svg = diagram.ToSvg();
        Assert.Contains("radarCurve-0", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("radarCurve-1", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("radarLegend", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void EmptyAndFlatRadars()
    {
        DiagramAssert.Svg(new RadarDiagram());

        // The maximum is the minimum: Mermaid divides by zero, and draws no curve
        var flat = new RadarDiagram { Ticks = 0 };
        flat.AddAxis("a");
        flat.AddAxis("b");
        flat.AddCurve("c", null, 0, 0);
        var svg = flat.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("radarCurve-0\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"radarGraticule\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void OptionsAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { Ticks = 33 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { Ticks = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { Maximum = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { Minimum = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { CurveTension = 2 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new RadarDiagram { CurveOpacity = -0.1 });
        Assert.Throws<ArgumentException>("name", () => new RadarAxis(""));
        Assert.Throws<ArgumentNullException>("name", () => new RadarCurve(null!));
        Assert.Throws<ArgumentNullException>("values", () => new RadarDiagram().AddCurve("c", null, null!));
    }
}
