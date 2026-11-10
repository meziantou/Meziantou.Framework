namespace Meziantou.Framework.Mermaid.Tests;

public sealed class RadarDiagramParserTests
{
    private static RadarChart Parse(string text) => (RadarChart)MermaidDocument.Parse(text).Content;

    [Fact]
    public void AxesAndCurves()
    {
        #region Mermaid_RadarDiagram
        var diagram = (RadarChart)MermaidDocument.Parse("""
            radar-beta
              title Restaurant Comparison
              axis food["Food Quality"], service["Service"], price["Price"]
              axis ambiance["Ambiance"]
              curve a["Restaurant A"]{4, 3, 2, 4}
              curve b["Restaurant B"]{ price: 3, service: 4, ambiance: 3, food: 3 }
              graticule polygon
              max 5
            """).Content;
        #endregion

        Assert.Equal("Restaurant Comparison", diagram.Title);
        Assert.Equal([("food", "Food Quality"), ("service", "Service"), ("price", "Price"), ("ambiance", "Ambiance")], diagram.Axes.Select(axis => (axis.Name, axis.Label)));
        Assert.Equal([4.0, 3, 2, 4], diagram.Curves[0].Values.AsEnumerable());
        Assert.Equal([3.0, 4, 3, 3], diagram.Curves[1].Values.AsEnumerable());
        Assert.Equal(("b", "Restaurant B"), (diagram.Curves[1].Name, diagram.Curves[1].Label));
        Assert.Equal((RadarGraticule.Polygon, (double?)5, 0.0, 5, true), (diagram.Graticule, diagram.Maximum, diagram.Minimum, diagram.Ticks, diagram.ShowLegend));
    }

    [Fact]
    public void LangiumTokens()
    {
        // The statements do not need their own lines, the colon of the entries is optional, and the entries can span lines
        var diagram = Parse("radar-beta: axis a , b-c_1 [ 'B' ] curve x{\n 1.5,\n 2\n} , y [\"Y\"] { b-c_1 7 , a: 8 } showLegend false, ticks 3 min 1 max 10 %% comment\n  accTitle: Accessible");
        Assert.Equal([("a", null), ("b-c_1", "B")], diagram.Axes.Select(axis => (axis.Name, axis.Label)));
        Assert.Equal([1.5, 2], diagram.Curves[0].Values.AsEnumerable());
        Assert.Equal([8.0, 7], diagram.Curves[1].Values.AsEnumerable());
        Assert.Equal((false, 3, 1.0, (double?)10), (diagram.ShowLegend, diagram.Ticks, diagram.Minimum, diagram.Maximum));
        Assert.Equal("Accessible", diagram.AccessibleTitle);

        // The axes of the entries by axis can come after the curve, and the last value of an option wins
        var later = Parse("radar-beta :\ncurve c{ q: 1, p: 2, z: 3 }\naxis p, q\nticks 2\nticks 4\ngraticule circle");
        Assert.Equal([2.0, 1], later.Curves[0].Values.AsEnumerable());
        Assert.Equal((4, RadarGraticule.Circle), (later.Ticks, later.Graticule));

        // Identifiers that only start with a keyword
        var prefixed = Parse("radar-beta\naxis axes, maximum, curves, minimal\ncurve curve1{1, 2, 3, 4}");
        Assert.Equal(["axes", "maximum", "curves", "minimal"], prefixed.Axes.Select(axis => axis.Name));
    }

    [Fact]
    public void ConfigurationAndTheme()
    {
        var document = MermaidDocument.Parse("""
            ---
            config:
              radar:
                width: 400
                height: 300
                marginTop: 10
                marginRight: 20
                marginBottom: 30
                marginLeft: 40
                axisScaleFactor: 0.25
                axisLabelFactor: 1.2
                curveTension: 0.1
              theme: base
              themeVariables:
                cScale0: "#FF0000"
                radar:
                  axisColor: "#00ff00"
                  axisStrokeWidth: 3
                  axisLabelFontSize: 14
                  curveOpacity: 0
                  curveStrokeWidth: 4
                  graticuleColor: blue
                  graticuleStrokeWidth: 2
                  graticuleOpacity: 0.5
                  legendFontSize: 16
            ---
            radar-beta
              axis A, B, C
              curve c1{1,2,3}
            """);
        var diagram = (RadarChart)document.Content;
        Assert.Empty(document.Warnings);
        Assert.Equal((400.0, 300.0, 10.0, 20.0, 30.0, 40.0), (diagram.Width, diagram.Height, diagram.MarginTop, diagram.MarginRight, diagram.MarginBottom, diagram.MarginLeft));
        Assert.Equal((0.25, 1.2, 0.1), (diagram.AxisScaleFactor, diagram.AxisLabelFactor, diagram.CurveTension));
        Assert.Equal((SceneColor.Parse("#00ff00"), (double?)3, (double?)14, (double?)0, (double?)4), (diagram.AxisColor, diagram.AxisStrokeWidth, diagram.AxisLabelFontSize, diagram.CurveOpacity, diagram.CurveStrokeWidth));
        Assert.Equal((SceneColor.Parse("blue"), (double?)2, (double?)0.5, (double?)16), (diagram.GraticuleColor, diagram.GraticuleStrokeWidth, diagram.GraticuleOpacity, diagram.LegendFontSize));
        Assert.Equal(SceneColor.Parse("#FF0000"), diagram.Theme.GetPaletteColor(0));
    }

    [Fact]
    public void TicksAreLimited()
    {
        var document = MermaidDocument.Parse("radar-beta\naxis a\nticks 40");
        Assert.Equal(32, ((RadarChart)document.Content).Ticks);
        Assert.Single(document.Warnings);

        var fraction = MermaidDocument.Parse("radar-beta\nticks 2.5");
        Assert.Equal(3, ((RadarChart)fraction.Content).Ticks);
        Assert.Single(fraction.Warnings);
    }

    [Theory]
    [InlineData("radar-beta\n  axis", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis max", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis circles", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis titles", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis 1a", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis é", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis a,\n b", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  axis a[B]", "a quoted label is expected.")]
    [InlineData("radar-beta\n  axis a[\"B\"", "']' is expected.")]
    [InlineData("radar-beta\n  curve c", "'{' is expected.")]
    [InlineData("radar-beta\n  curve c{}", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  curve c{1, a: 2}", "a number is expected.")]
    [InlineData("radar-beta\n  axis a\n  curve c{a: 1, 2}", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  curve c{1\n, 2}", "'}' is expected.")]
    [InlineData("radar-beta\n  curve c{-1}", "the name of an axis is expected.")]
    [InlineData("radar-beta\n  curve c{01}", "it cannot start with 0.")]
    [InlineData("radar-beta\n  curve c{a: 1}", "Axes must be populated before curves for reference entries.")]
    [InlineData("radar-beta\n  axis a, b[\"Bee\"]\n  curve c{a: 1}", "Missing entry for axis Bee.")]
    [InlineData("radar-beta\n  showLegend yes", "'true' or 'false' is expected.")]
    [InlineData("radar-beta\n  graticule square", "'circle' or 'polygon' is expected.")]
    [InlineData("radar-beta\n  max -1", "a number is expected.")]
    [InlineData("radar-beta\n  title x y\n  bogus", "'axis', 'curve' or an option is expected.")]
    [InlineData("radar-betax", "No diagram type was found")]
    [InlineData("radar-beta;", "Unexpected ';'.")]
    public void InvalidRadarDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new RadarChart
        {
            Title = "Grades",
            AccessibleTitle = "Accessible",
            ShowLegend = false,
            Ticks = 0,
            Maximum = 100.5,
            Minimum = 0.000001,
            Graticule = RadarGraticule.Polygon,
            Width = 500,
            Height = 400,
            MarginTop = 1,
            MarginRight = 2,
            MarginBottom = 3,
            MarginLeft = 4,
            AxisScaleFactor = 0.5,
            AxisLabelFactor = 1.5,
            CurveTension = 0,
            AxisColor = SceneColor.Parse("#112233"),
            AxisStrokeWidth = 1,
            AxisLabelFontSize = 10,
            CurveOpacity = 0.25,
            CurveStrokeWidth = 5,
            GraticuleColor = SceneColor.Parse("#445566"),
            GraticuleStrokeWidth = 0.5,
            GraticuleOpacity = 1,
            LegendFontSize = 20,
            Theme = DiagramTheme.Neutral,
        };
        diagram.AddAxis("m", "Math \"1\"");
        diagram.AddAxis("s-2");
        diagram.AddCurve("alice", "Alice\nSmith", 85, 1e22);
        diagram.AddCurve("_bob", null, 70);

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (RadarChart)parsed.Content;
        Assert.Equal(("Grades", "Accessible", false, 0, (double?)100.5, 0.000001, RadarGraticule.Polygon), (result.Title, result.AccessibleTitle, result.ShowLegend, result.Ticks, result.Maximum, result.Minimum, result.Graticule));
        Assert.Equal((500.0, 400.0, 1.0, 2.0, 3.0, 4.0, 0.5, 1.5, 0.0), (result.Width, result.Height, result.MarginTop, result.MarginRight, result.MarginBottom, result.MarginLeft, result.AxisScaleFactor, result.AxisLabelFactor, result.CurveTension));
        Assert.Equal((SceneColor.Parse("#112233"), (double?)1, (double?)10, (double?)0.25, (double?)5), (result.AxisColor, result.AxisStrokeWidth, result.AxisLabelFontSize, result.CurveOpacity, result.CurveStrokeWidth));
        Assert.Equal((SceneColor.Parse("#445566"), (double?)0.5, (double?)1, (double?)20), (result.GraticuleColor, result.GraticuleStrokeWidth, result.GraticuleOpacity, result.LegendFontSize));
        Assert.Equal([("m", "Math \"1\""), ("s-2", null)], result.Axes.Select(axis => (axis.Name, axis.Label)));
        Assert.Equal([("alice", "Alice\nSmith"), ("_bob", null)], result.Curves.Select(curve => (curve.Name, curve.Label)));
        Assert.Equal([85.0, 1e22], result.Curves[0].Values.AsEnumerable());
        Assert.Same(DiagramTheme.Neutral, result.Theme);
    }

    [Fact]
    public void WriteEscapesDirectivesInLabels()
    {
        var diagram = new RadarChart();
        diagram.AddAxis("a", "Axis %%{init: {theme: dark}}%%");
        diagram.AddAxis("b", "Unclosed %%{");
        diagram.AddCurve("c", "100%%", 1, 2);

        var text = MermaidWriter.Write(diagram);
        Assert.DoesNotContain("%%", text, StringComparison.Ordinal);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (RadarChart)parsed.Content;
        Assert.Equal(["Axis %%{init: {theme: dark}}%%", "Unclosed %%{"], result.Axes.Select(axis => axis.Label));
        Assert.Equal("100%%", result.Curves[0].Label);
        Assert.Same(DiagramTheme.Default, result.Theme);
    }

    [Theory]
    [InlineData("1a")]
    [InlineData("a b")]
    [InlineData("a-")]
    [InlineData("min")]
    [InlineData("polygonal")]
    [InlineData("accTitle")]
    public void WriteRejectsInvalidNames(string name)
    {
        var diagram = new RadarChart();
        diagram.AddAxis(name);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(diagram));
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var empty = new RadarChart();
        empty.AddCurve("c", null);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(empty));

        var negative = new RadarChart();
        negative.AddCurve("c", null, -1);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(negative));
    }
}
