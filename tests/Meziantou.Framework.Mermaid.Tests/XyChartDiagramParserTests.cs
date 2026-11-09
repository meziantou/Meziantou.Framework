namespace Meziantou.Framework.Mermaid.Tests;

public sealed class XyChartDiagramParserTests
{
    private static XyChart Parse(string text) => (XyChart)MermaidDocument.Parse(text).Content;

    [Fact]
    public void BarsAndLine()
    {
        #region Mermaid_XyChartDiagram
        var diagram = (XyChart)MermaidDocument.Parse("""
            xychart
                title "Sales Revenue"
                x-axis [jan, feb, mar, apr, may, jun]
                y-axis "Revenue (in $)" 4000 --> 11000
                bar [5000, 6000, 7500, 8200, 9500, 10500]
                line [5000, 6000, 7500, 8200, 9500, 10500]
            """).Content;
        #endregion

        Assert.Equal("Sales Revenue", diagram.Title);
        Assert.Equal(["jan", "feb", "mar", "apr", "may", "jun"], diagram.XAxis.Categories);
        Assert.Equal(("Revenue (in $)", new XyChartAxisRange(4000, 11000)), (diagram.YAxis.Title, diagram.YAxis.Range));
        Assert.IsType<XyChartBarSeries>(diagram.Series[0]);
        Assert.IsType<XyChartLineSeries>(diagram.Series[1]);
        Assert.Equal([5000.0, 6000, 7500, 8200, 9500, 10500], diagram.Series[1].Values);
    }

    [Fact]
    public void NamesLabelsAndOrientation()
    {
        var diagram = Parse("""
            xychart-beta horizontal
              accTitle: Accessible
              accDescr: Described
              x-axis xAxisName 45.5 --> .34
              y-axis yAxisName
              line "  avg  " [+1.3, .6, 2.4 " peak ", -.34]
              bar barName [1, 2 "ignored"]
            """);
        Assert.Equal(XyChartOrientation.Horizontal, diagram.Orientation);
        Assert.Equal(("Accessible", "Described"), (diagram.AccessibleTitle, diagram.Description));
        Assert.Equal(("xAxisName", new XyChartAxisRange(45.5, 0.34)), (diagram.XAxis.Title, diagram.XAxis.Range));
        Assert.Equal(("yAxisName", (XyChartAxisRange?)null), (diagram.YAxis.Title, diagram.YAxis.Range));
        var line = Assert.IsType<XyChartLineSeries>(diagram.Series[0]);
        Assert.Equal("avg", line.Name);
        Assert.Equal([1.3, 0.6, 2.4, -0.34], line.Values);
        Assert.Equal([null, null, "peak", null], line.PointLabels);
        Assert.Equal("barName", diagram.Series[1].Name);
        Assert.Single(MermaidDocument.Parse("xychart\nbar [1, 2 \"ignored\"]").Warnings);
    }

    [Fact]
    public void LexerQuirksOfMermaid()
    {
        // The pieces of a word are joined without their spaces, and the keywords ignore the case
        var diagram = Parse("xychart\nTITLE This is joined\nX-AXIS [a b, \"c d\", e-f_g, bar1]\nBAR barName [1, 2, 3]\nline linear [1]");
        Assert.Equal("Thisisjoined", diagram.Title);
        Assert.Equal(["ab", "c d", "e-f_g", "bar1"], diagram.XAxis.Categories);
        Assert.Equal(["barName", "linear"], diagram.Series.Select(series => series.Name));

        // A statement does not need a line feed, and the entity codes are decoded in quotes
        diagram = Parse("xychart\ntitle \"a #35; #quot;b#quot; #9829; #unknown;\" line [1] ; bar [2]");
        Assert.Equal("a # \"b\" ♥ &unknown;", diagram.Title);
        Assert.HasCount(2, diagram.Series);

        // A later axis statement replaces the title and the values, and keeps the other part
        diagram = Parse("xychart\nx-axis first [a, b]\nx-axis second\nx-axis 1 --> 5\n");
        Assert.Equal((null, new XyChartAxisRange(1, 5)), (diagram.XAxis.Title, diagram.XAxis.Range));
        Assert.Empty(diagram.XAxis.Categories);

        // An unterminated quoted text ends with the text, and an empty quoted text is no text
        Assert.Equal("open", Parse("xychart\ntitle \"open").Title);
        Assert.Null(Parse("xychart\nx-axis \"\" [a]").XAxis.Title);

        // Markdown texts are read as plain texts
        Assert.Equal("**bold**", Parse("xychart\ntitle \"`**bold**`\"").Title);

        // The lines of a description block are trimmed, and its comments removed
        Assert.Equal("a\nb", Parse("xychart\naccDescr {%% comment\n  a\n  b }\nbar [1]").Description);
    }

    [Fact]
    public void Configuration()
    {
        var diagram = Parse("""
            ---
            title: From the front matter
            config:
              xyChart:
                width: 900
                height: 600
                showDataLabel: true
                showDataLabelOutsideBar: true
                showLegend: false
                chartOrientation: horizontal
                plotReservedSpacePercent: 60
                xAxis:
                  labelRotation: 45
                  showTick: false
                yAxis:
                  axisLineWidth: 3
                  labelRotation: 400
              themeVariables:
                xyChart:
                  titleColor: '#ff0000'
                  yAxisLineColor: '#00ff00'
                  plotColorPalette: '#000000, #0000FF'
            ---
            xychart
                line [1, 2]
            """);
        Assert.Equal("From the front matter", diagram.Title);
        Assert.Equal((900.0, 600.0, true, true, false), (diagram.Width, diagram.Height, diagram.ShowDataLabels, diagram.DataLabelsOutsideBars, diagram.ShowLegend));
        Assert.Equal((XyChartOrientation.Horizontal, 60.0), (diagram.Orientation, diagram.PlotReservedSpacePercent));
        Assert.Equal((45.0, false, 3.0, 0.0), (diagram.XAxis.LabelRotation, diagram.XAxis.ShowTicks, diagram.YAxis.LineWidth, diagram.YAxis.LabelRotation));
        Assert.Equal((SceneColor.Parse("#ff0000"), SceneColor.Parse("#00ff00")), (diagram.TitleColor, diagram.YAxis.LineColor));
        Assert.Equal([SceneColor.Parse("#000000"), SceneColor.Parse("#0000ff")], diagram.PlotColors);

        var invalid = MermaidDocument.Parse("---\nconfig:\n  xyChart:\n    width: -5\n  themeVariables:\n    xyChart:\n      plotColorPalette: 'red, nope'\n---\nxychart\nbar [1]");
        Assert.HasCount(2, invalid.Warnings);
        Assert.Empty(((XyChart)invalid.Content).PlotColors);
    }

    [Theory]
    [InlineData("xychart abc", "(1, 9): Unexpected 'abc': a statement is expected.")]
    [InlineData("xychart\nx-axis xAxisName aaa --> 33", "(2, 22): Unexpected '-->': a statement is expected.")]
    [InlineData("xychart\ny-axis yAxisName [ 45.3, 33 ]", "(2, 18): Unexpected '[': a statement is expected.")]
    [InlineData("xychart\nx-axis [\"cat1\" [ cat2a ]", "(2, 16): Unexpected '[': ',' or ']' is expected.")]
    [InlineData("xychart\nline \"name\"", "(2, 12): Unexpected the end of the line: '[' is expected.")]
    [InlineData("xychart\nbar [ ]", "(2, 7): Unexpected ']': a number is expected.")]
    [InlineData("xychart\nbar [1,\n2]", "(2, 8): Unexpected the end of the line: a number is expected.")]
    [InlineData("xychart\ntitle", "(2, 6): Unexpected the end of the line: a text is expected.")]
    [InlineData("xychart\ntitle a#35;", "(2, 8): An entity code such as '#35;' must be in a quoted text.")]
    [InlineData("xychart\nx-axis line [a]", "(2, 8): Unexpected 'line': a title, '[' or a number is expected.")]
    [InlineData("xychart\nx-axis verticalAxis [a]", "(2, 8): Unexpected 'vertical': a title, '[' or a number is expected.")]
    [InlineData("xychart\nx-axis Q1 [a]", "(2, 11): Unexpected '[': '-->' is expected.")]
    [InlineData("xychart\ntitle \"`a\nb`\"", "(2, 10): Unexpected character 'end of line'.")]
    [InlineData("xychart\nline [1 \"`md`\"]", "(2, 11): Unexpected a quoted text: ',' or ']' is expected.")]
    [InlineData("xychart\ny-axis 1e5 --> 2", "(2, 9): Unexpected 'e': '-->' is expected.")]
    [InlineData("xychart\nbar [999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999999]", "(2, 6): The number")]
    [InlineData("xychart\ntitle a\nxychart", "(3, 1): Unexpected 'xychart': a statement is expected.")]
    [InlineData("xychart\n§", "(2, 1): Unexpected character '§'.")]
    public void InvalidXyCharts(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new XyChart
        {
            Title = "  Revenue #1 \"2024\" %%{  ",
            Description = "Line one\nLine two",
            Orientation = XyChartOrientation.Horizontal,
            Width = 640,
            ShowDataLabels = true,
            TitleColor = SceneColor.Parse("#123456"),
        };
        diagram.PlotColors.Add(SceneColor.Parse("#ff0000"));
        diagram.PlotColors.Add(SceneColor.Parse("#00ff0080"));
        diagram.XAxis.Title = "Q1 numbers";
        diagram.XAxis.Categories.Add("title");
        diagram.XAxis.Categories.Add("plain");
        diagram.XAxis.Categories.Add("with space");
        diagram.XAxis.LabelRotation = -30;
        diagram.YAxis.Range = new XyChartAxisRange(-1e-7, 1e22);
        diagram.YAxis.LineColor = SceneColor.Parse("#abcdef");
        diagram.AddBars([1, -0.0, 2.5], "linear");
        var line = diagram.AddLine([1e21, 0.000001, 3]);
        line.PointLabels.Add("first");
        line.PointLabels.Add(null);
        line.PointLabels.Add("\tlast\n");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        Assert.Contains("1000000000000000000000", text, StringComparison.Ordinal);
        Assert.Contains("-0.0000001 --> 10000000000000000000000", text, StringComparison.Ordinal);
        var result = (XyChart)parsed.Content;
        Assert.Equal((diagram.Title, diagram.Description, XyChartOrientation.Horizontal, 640.0, true), (result.Title, result.Description, result.Orientation, result.Width, result.ShowDataLabels));
        Assert.Equal((diagram.TitleColor, diagram.YAxis.LineColor), (result.TitleColor, result.YAxis.LineColor));
        Assert.Equal(diagram.PlotColors, result.PlotColors);
        Assert.Equal(("Q1 numbers", -30.0), (result.XAxis.Title, result.XAxis.LabelRotation));
        Assert.Equal(["title", "plain", "with space"], result.XAxis.Categories);
        Assert.Equal(diagram.YAxis.Range, result.YAxis.Range);
        Assert.Equal("linear", result.Series[0].Name);
        Assert.Equal([1.0, -0.0, 2.5], result.Series[0].Values);
        Assert.Equal(["first", null, "\tlast\n"], ((XyChartLineSeries)result.Series[1]).PointLabels);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var withCategories = new XyChart();
        withCategories.YAxis.Categories.Add("a");
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(withCategories));

        var empty = new XyChart();
        empty.AddLine([]);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(empty));

        var notFinite = new XyChart();
        notFinite.AddBars([double.NaN]);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(notFinite));

        var range = new XyChart();
        range.YAxis.Range = new XyChartAxisRange(0, double.PositiveInfinity);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(range));
    }
}
