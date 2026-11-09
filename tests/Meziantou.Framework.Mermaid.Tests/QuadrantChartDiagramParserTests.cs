using Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

namespace Meziantou.Framework.Mermaid.Tests;

public sealed class QuadrantChartDiagramParserTests
{
    private static QuadrantChartDiagram Parse(string text) => (QuadrantChartDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void QuadrantChart()
    {
        #region Mermaid_QuadrantChartDiagram
        var diagram = (QuadrantChartDiagram)MermaidDocument.Parse("""
            quadrantChart
                title Reach and engagement of campaigns
                x-axis Low Reach --> High Reach
                y-axis Low Engagement --> High Engagement
                quadrant-1 We should expand
                quadrant-2 Need to promote
                quadrant-3 Re-evaluate
                quadrant-4 May be improved
                Campaign A: [0.3, 0.6]
                Campaign B:::important: [0.45, 0.23] radius: 12, color: #ff3300
                classDef important stroke-color: #310085, stroke-width: 2px
            """).Diagram;
        #endregion

        Assert.Equal("Reach and engagement of campaigns", diagram.Title);
        Assert.Equal(("Low Reach", "High Reach", "Low Engagement", "High Engagement"), (diagram.XAxisLeftLabel?.Text, diagram.XAxisRightLabel?.Text, diagram.YAxisBottomLabel?.Text, diagram.YAxisTopLabel?.Text));
        Assert.Equal(["We should expand", "Need to promote", "Re-evaluate", "May be improved"], new[] { diagram.Quadrant1Label, diagram.Quadrant2Label, diagram.Quadrant3Label, diagram.Quadrant4Label }.Select(label => label?.Text));
        Assert.Equal([("Campaign A", 0.3, 0.6), ("Campaign B", 0.45, 0.23)], diagram.Points.Select(point => (point.Label.Text, point.X, point.Y)));
        var point = diagram.Points[1];
        Assert.Equal(("important", 12, SceneColor.Parse("#ff3300")), (point.ClassName, point.Style.Radius, point.Style.Color));
        Assert.Equal((SceneColor.Parse("#310085"), 2), (diagram.ClassDefinitions["important"].StrokeColor, diagram.ClassDefinitions["important"].StrokeWidth));
    }

    [Fact]
    public void ConfigurationAndThemeVariables()
    {
        var diagram = Parse("""
            ---
            config:
              quadrantChart:
                chartWidth: 400
                chartHeight: 300
                pointRadius: 8
                xAxisPosition: bottom
                yAxisPosition: right
                quadrantLabelFontSize: 20
              themeVariables:
                quadrant1Fill: "#ff0000"
                quadrantPointFill: "#00ff00"
                quadrant1TextFill: "ff0000"
            ---
            quadrantChart
              accTitle: Accessible
              accDescr {
                The first line
                The second line
              }
            """);
        Assert.Equal((400.0, 300.0, 8.0, 20.0), (diagram.Width, diagram.Height, diagram.PointRadius, diagram.QuadrantLabelFontSize));
        Assert.Equal((QuadrantChartXAxisPosition.Bottom, QuadrantChartYAxisPosition.Right), (diagram.XAxisPosition, diagram.YAxisPosition));
        Assert.Equal((SceneColor.Parse("#ff0000"), SceneColor.Parse("#00ff00")), (diagram.Colors.Quadrant1Fill, diagram.Colors.PointFill));

        // Mermaid passes a color without '#' to the browser, which ignores it
        Assert.Null(diagram.Colors.Quadrant1TextFill);
        Assert.Equal(("Accessible", "The first line\nThe second line"), (diagram.AccessibleTitle, diagram.Description));
    }

    [Fact]
    public void TextsAsMermaidReadsThem()
    {
        var diagram = Parse("""
            quadrantChart title On the line of the keyword
              X-AXIS "Quoted text" --> "`**Markdown**`"
              y-axis Bottom -->
              QUADRANT-2 A b-c+d=e*f#g_h.i&j!k
              "" Empty string is ignored: [0, 1]
              é ünïcode: [0.5, 1] %% comment
              Point 1:[0.5,1]; Point 2: [ 0.25 , 0 ] radius: 007
            """);
        Assert.Equal("On the line of the keyword", diagram.Title);
        Assert.Equal(("Quoted text", DiagramTextFormat.Plain), (diagram.XAxisLeftLabel!.Text, diagram.XAxisLeftLabel.Format));
        Assert.Equal(("**Markdown**", DiagramTextFormat.Markdown), (diagram.XAxisRightLabel!.Text, diagram.XAxisRightLabel.Format));
        Assert.Equal("Bottom \u27F6", diagram.YAxisBottomLabel!.Text);
        Assert.Equal("A b-c+d=e*f#g_h.i&j!k", diagram.Quadrant2Label!.Text);
        Assert.Equal(["Empty string is ignored", "\u00E9 \u00FCn\u00EFcode", "Point 1", "Point 2"], diagram.Points.Select(point => point.Label.Text));
        Assert.Equal(7, diagram.Points[3].Style.Radius);
    }

    [Fact]
    public void LaterStatementsReplaceEarlierOnes()
    {
        var diagram = Parse("""
            ---
            title: Front matter
            ---
            quadrantChart
              title
              x-axis A --> B
              x-axis C
              classDef c radius: 3
              classDef c color: #fff
            """);
        Assert.Null(diagram.Title);
        Assert.Equal(("C", "B"), (diagram.XAxisLeftLabel?.Text, diagram.XAxisRightLabel?.Text));
        Assert.Equal((null, SceneColor.White), (diagram.ClassDefinitions["c"].Radius, diagram.ClassDefinitions["c"].Color));
    }

    [Theory]
    [InlineData("quadrantChart\n  A: [1.0, 0.5]", "',' is expected between the coordinates of the point.")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5, 0.6]", "']' is expected after the coordinates of the point.")]
    [InlineData("quadrantChart\n  A: [2, 0.5]", "The x coordinate of the point is expected")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] foo: 1", "style named foo is not supported.")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] COLOR: #fff", "style named COLOR is not supported.")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] radius", "value for radius undefined is invalid, please use a valid number")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] color: #12", "value for color #12 is invalid, please use a valid hex code")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] stroke-width: 3", "value for stroke-width 3 is invalid, please use a valid number of pixels (eg. 10px)")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] radius: 99999999", "is too large")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] radius: 1.5", "Unexpected '.'.")]
    [InlineData("quadrantChart\n  A: [0.5, 0.5] radius: 3,", "Unexpected 'end of text'.")]
    [InlineData("quadrantChart\n  my title: [0.5, 0.5]", "The position of the point is expected")]
    [InlineData("quadrantChart\n  A(1): [0.5, 0.5]", "Unexpected character '('.")]
    [InlineData("quadrantChart\n  -A: [0.5, 0.5]", "Unexpected '-'.")]
    [InlineData("quadrantChart\n  x-axis a \"b\"", "Unexpected 'b'.")]
    [InlineData("quadrantChart\n  x-axis", "A text is expected, not 'end of text'.")]
    [InlineData("quadrantChart\n  classDef  c radius: 3", "The name of the class is expected.")]
    [InlineData("quadrantChart\n  A:::: [0.5, 0.5]", "A class name is expected after ':::'.")]
    [InlineData("quadrantChart\n  \"unclosed: [0.5, 0.5]", "The string is not closed.")]
    [InlineData("quadrantChart\n  \"`unclosed: [0.5, 0.5]", "The markdown string is not closed")]
    [InlineData("quadrantChart\n  accDescr {", "The description is not closed by '}'.")]
    [InlineData("quadrantChart\n  quadrantChart", "Unexpected")]
    public void InvalidQuadrantCharts(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new QuadrantChartDiagram
        {
            Title = "Title",
            Description = "Description",
            XAxisRightLabel = "Only the right text",
            YAxisBottomLabel = "--> is not a delimiter in quotes",
            YAxisTopLabel = new DiagramText("Markdown", DiagramTextFormat.Markdown),
            Quadrant1Label = "title is a keyword",
            Quadrant2Label = "  trimmed  ",
            Width = 300,
            XAxisPosition = QuadrantChartXAxisPosition.Bottom,
        };
        diagram.Colors.Quadrant4Fill = SceneColor.Parse("#123456");
        diagram.AddPoint("Plain words", 0.000001, 1).Style.StrokeWidth = 2;
        diagram.AddPoint(string.Empty, 0.5, 0.25).ClassName = "cls";
        diagram.AddPoint("Two\nlines", 0, 0.123456789);
        diagram.ClassDefinitions["cls"] = new QuadrantChartPointStyle { Radius = 4, Color = SceneColor.Parse("#abc"), StrokeColor = SceneColor.Black };
        diagram.ClassDefinitions["empty"] = new QuadrantChartPointStyle();

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (QuadrantChartDiagram)parsed.Diagram;
        Assert.Equal(("Title", "Description", 300.0, QuadrantChartXAxisPosition.Bottom), (result.Title, result.Description, result.Width, result.XAxisPosition));
        Assert.Null(result.XAxisLeftLabel);
        Assert.Equal(["Only the right text", "--> is not a delimiter in quotes", "Markdown", "title is a keyword", "trimmed"], new[] { result.XAxisRightLabel, result.YAxisBottomLabel, result.YAxisTopLabel, result.Quadrant1Label, result.Quadrant2Label }.Select(label => label?.Text));
        Assert.Equal(DiagramTextFormat.Markdown, result.YAxisTopLabel!.Format);
        Assert.Equal([("Plain words", 0.000001, 1.0), (string.Empty, 0.5, 0.25), ("Two\nlines", 0, 0.123456789)], result.Points.Select(point => (point.Label.Text, point.X, point.Y)));
        Assert.Equal(SceneColor.Parse("#123456"), result.Colors.Quadrant4Fill);
        Assert.Equal((4, SceneColor.Parse("#aabbcc"), SceneColor.Black), (result.ClassDefinitions["cls"].Radius, result.ClassDefinitions["cls"].Color, result.ClassDefinitions["cls"].StrokeColor));
        Assert.False(result.ClassDefinitions.ContainsKey("empty"));
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1.0, "1")]
    [InlineData(0.5, "0.5")]
    [InlineData(1e-7, "0.0000001")]
    [InlineData(1e-30, "0")]
    public void FormatCoordinate(double value, string expected)
    {
        Assert.Equal(expected, QuadrantChartDiagramWriter.FormatCoordinate(value));
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var quote = new QuadrantChartDiagram { Quadrant1Label = "a \"quote\"" };
        Assert.Throws<ArgumentException>("label", () => MermaidWriter.Write(quote));
        // Mermaid reads "%% as a comment that also takes the quote
        var comment = new QuadrantChartDiagram { Quadrant1Label = "%%]" };
        Assert.Throws<ArgumentException>("label", () => MermaidWriter.Write(comment));
        var backquote = new QuadrantChartDiagram { Quadrant1Label = new DiagramText("a `b`", DiagramTextFormat.Markdown) };
        Assert.Throws<ArgumentException>("label", () => MermaidWriter.Write(backquote));
        var className = new QuadrantChartDiagram();
        className.AddPoint("p", 0, 0).ClassName = "a-b";
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(className));
        var classDefinition = new QuadrantChartDiagram();
        classDefinition.ClassDefinitions["a b"] = new QuadrantChartPointStyle { Radius = 1 };
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(classDefinition));
        var transparent = new QuadrantChartDiagram();
        transparent.AddPoint("p", 0, 0).Style.Color = SceneColor.Transparent;
        Assert.Throws<ArgumentException>("color", () => MermaidWriter.Write(transparent));
    }
}
