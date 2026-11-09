namespace Meziantou.Framework.Mermaid.Tests;

public sealed class PieDiagramParserTests
{
    private static PieChart Parse(string text) => (PieChart)MermaidDocument.Parse(text).Content;

    [Fact]
    public void Slices()
    {
        #region Mermaid_PieDiagram
        var diagram = (PieChart)MermaidDocument.Parse("""
            pie showData
                title Pets adopted by volunteers
                "Dogs" : 386
                "Cats" : 85
                "Rats" : 15
            """).Content;
        #endregion

        Assert.Equal("Pets adopted by volunteers", diagram.Title);
        Assert.True(diagram.ShowData);
        Assert.Equal([("Dogs", 386.0), ("Cats", 85.0), ("Rats", 15.0)], diagram.Slices.Select(slice => (slice.Label, slice.Value)));
    }

    [Fact]
    public void StatementsOnTheLineOfTheKeyword()
    {
        var diagram = Parse("pie title Key elements\n\"a\":1.5 %% comment\n'b \\'c\\'' : 0\n\"d\" : -0");
        Assert.Equal("Key elements", diagram.Title);
        Assert.False(diagram.ShowData);
        Assert.Equal([("a", 1.5), ("b 'c'", 0.0), ("d", 0.0)], diagram.Slices.Select(slice => (slice.Label, slice.Value)));
        Assert.False(double.IsNegative(diagram.Slices[2].Value));

        var slice = Parse("pie \"x\" : 2");
        Assert.Equal("x", slice.Slices[0].Label);
    }

    [Fact]
    public void ConfigurationAndThemeVariables()
    {
        var document = MermaidDocument.Parse("""
            ---
            config:
              pie:
                textPosition: 0.5
                donutHole: 0.2
                legendPosition: bottom
                highlightSlice: Potassium
              themeVariables:
                pie1: "#ff0000"
                pie12: "#00ff00"
                pieStrokeColor: white
                pieStrokeWidth: 3px
                pieOuterStrokeColor: "#123456"
                pieOuterStrokeWidth: "5px"
                pieOpacity: "0.5"
                pieSectionTextColor: red
                pieSectionTextSize: 12px
                pieLegendTextColor: blue
                pieLegendTextSize: 20px
                pieTitleTextSize: 30px
            ---
            pie
                accTitle: Accessible
                accDescr: The description
                "Potassium" : 50.05
            """);
        var diagram = (PieChart)document.Content;
        Assert.Empty(document.Warnings);
        Assert.Equal((0.5, 0.2, PieLegendPosition.Bottom, "Potassium"), (diagram.TextPosition, diagram.DonutHole, diagram.LegendPosition, diagram.HighlightedSlice));
        Assert.Equal(SceneColor.Parse("#ff0000"), diagram.SliceColors[0]);
        Assert.Equal(SceneColor.Parse("#00ff00"), diagram.SliceColors[11]);
        Assert.Equal((SceneColor.White, 3.0, SceneColor.Parse("#123456"), 5.0, 0.5), (diagram.StrokeColor, diagram.StrokeWidth, diagram.OuterStrokeColor, diagram.OuterStrokeWidth, diagram.Opacity));
        Assert.Equal((SceneColor.Parse("red"), 12.0, SceneColor.Parse("blue"), 20.0), (diagram.SectionTextColor, diagram.SectionTextSize, diagram.LegendTextColor, diagram.LegendTextSize));
        Assert.Equal(("Accessible", "The description"), (diagram.AccessibleTitle, diagram.Description));
        Assert.Same(DiagramTheme.Default, diagram.Theme);
    }

    [Fact]
    public void UnsupportedConfigurationIsReported()
    {
        var document = MermaidDocument.Parse("""
            ---
            config:
              pie:
                textPosition: 2
                donutHole: 0.95
                legendPosition: middle
                highlightSlice: hover
            ---
            pie
                "a" : 1
                "a" : 2
            """);
        var diagram = (PieChart)document.Content;
        Assert.Equal((0.75, 0.0, PieLegendPosition.Right, null), (diagram.TextPosition, diagram.DonutHole, diagram.LegendPosition, diagram.HighlightedSlice));
        Assert.Equal(4, document.Warnings.Count);

        // A label keeps its first value, as in Mermaid
        Assert.Equal([("a", 1.0)], diagram.Slices.Select(slice => (slice.Label, slice.Value)));
    }

    [Theory]
    [InlineData("pie\n  \"a\" : -5", "\"a\" has invalid value: -5. Negative values are not allowed in pie charts.")]
    [InlineData("pie\n  a : 5", "a quoted label is expected.")]
    [InlineData("pie\n  \"a\" 5", "':' is expected.")]
    [InlineData("pie\n  \"a\" : 05", "it cannot start with 0.")]
    [InlineData("pie\n  \"a\" : 5.", "is not a valid number.")]
    [InlineData("pie\n  \"a\" : 1.5.2", "is not a valid number.")]
    [InlineData("pie\n  \"a\" : 1e5", "Unexpected 'e5'.")]
    [InlineData("pie\n  \"a\" : x", "a number is expected.")]
    [InlineData("pie\n  \"a\" : 1 \"b\" : 2", "Unexpected '\"b\"'.")]
    [InlineData("pie\n  \"a : 1", "The string is not closed.")]
    [InlineData("pie\n  showData", "a quoted label is expected.")]
    [InlineData("pie\n  \"a\" : 1;", "Unexpected ';'.")]
    [InlineData("pie:\n  \"a\" : 1", "'pie' is expected.")]
    public void InvalidPieDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooManySlices()
    {
        var text = "pie\n" + string.Concat(Enumerable.Range(0, 5).Select(i => $"\"s{i}\" : 1\n"));
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text, new MermaidParseOptions { MaxNodes = 4 }));
        Assert.Contains("more than 4 slices", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new PieChart
        {
            Title = "Elements",
            Description = "Line 1\nLine 2",
            ShowData = true,
            TextPosition = 0.5,
            DonutHole = 0.3,
            LegendPosition = PieLegendPosition.Center,
            HighlightedSlice = "Iron \"Fe\"",
            StrokeColor = SceneColor.White,
            StrokeWidth = 1.5,
            OuterStrokeColor = SceneColor.Parse("#123456"),
            OuterStrokeWidth = 4,
            Opacity = 0.9,
            SectionTextColor = SceneColor.Black,
            SectionTextSize = 12,
            LegendTextColor = SceneColor.Parse("#336699"),
            LegendTextSize = 15,
            Theme = DiagramTheme.Forest,
        };
        diagram.SliceColors[0] = SceneColor.Parse("#ff0000");
        diagram.SliceColors[11] = SceneColor.Parse("#00ff00");
        diagram.AddSlice("Iron \"Fe\"", 5);
        diagram.AddSlice("Line\nbreak\t'x'", 0.000001);
        diagram.AddSlice("Big", 1e25);
        diagram.AddSlice("Zero", 0);

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Empty(parsed.Warnings);
        Assert.Equal(text, MermaidWriter.Write(parsed.Content));
        var result = (PieChart)parsed.Content;
        Assert.Equal(("Elements", "Line 1\nLine 2", true, 0.5, 0.3, PieLegendPosition.Center, "Iron \"Fe\""), (result.Title, result.Description, result.ShowData, result.TextPosition, result.DonutHole, result.LegendPosition, result.HighlightedSlice));
        Assert.Equal((SceneColor.White, 1.5, SceneColor.Parse("#123456"), 4.0, 0.9), (result.StrokeColor, result.StrokeWidth, result.OuterStrokeColor, result.OuterStrokeWidth, result.Opacity));
        Assert.Equal((SceneColor.Black, 12.0, SceneColor.Parse("#336699"), 15.0), (result.SectionTextColor, result.SectionTextSize, result.LegendTextColor, result.LegendTextSize));
        Assert.Equal([(0, SceneColor.Parse("#ff0000")), (11, SceneColor.Parse("#00ff00"))], result.SliceColors.OrderBy(item => item.Key).Select(item => (item.Key, item.Value)));
        Assert.Equal([("Iron \"Fe\"", 5.0), ("Line\nbreak\t'x'", 0.000001), ("Big", 1e25), ("Zero", 0.0)], result.Slices.Select(slice => (slice.Label, slice.Value)));
        Assert.Same(DiagramTheme.Forest, result.Theme);
        Assert.Contains("\"Big\" : 10000000000000000000000000\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteRejectsWhatMermaidCannotRead()
    {
        var duplicate = new PieChart();
        duplicate.AddSlice("a", 1);
        duplicate.AddSlice("a", 2);
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(duplicate));

        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(new PieChart { HighlightedSlice = "hover" }));

        var color = new PieChart();
        color.SliceColors[12] = SceneColor.Black;
        Assert.Throws<ArgumentException>("diagram", () => MermaidWriter.Write(color));
    }
}
