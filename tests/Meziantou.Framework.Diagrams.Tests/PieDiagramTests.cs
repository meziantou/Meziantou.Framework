namespace Meziantou.Framework.Diagrams.Tests;

public sealed class PieDiagramTests
{
    [Fact]
    public void PieDiagram()
    {
        #region Diagrams_PieDiagram
        var diagram = new PieDiagram { Title = "Pets adopted by volunteers", ShowData = true };
        diagram.AddSlice("Dogs", 386);
        diagram.AddSlice("Cats", 85);
        diagram.AddSlice("Rats", 15);
        #endregion

        Assert.Equal(["Dogs", "Cats", "Rats"], diagram.Slices.Select(slice => slice.Label));
        DiagramAssert.Svg(diagram, "PieDiagram");
    }

    [Fact]
    public void DonutHighlightAndColors()
    {
        var diagram = new PieDiagram
        {
            DonutHole = 0.4,
            TextPosition = 0.5,
            HighlightedSlice = "Potassium",
            LegendPosition = PieLegendPosition.Left,
            StrokeColor = SceneColor.White,
            StrokeWidth = 1,
            OuterStrokeWidth = 5,
            Opacity = 1,
            SectionTextSize = 12,
            LegendTextSize = 14,
            LegendTextColor = SceneColor.Parse("#1f77b4"),
        };
        diagram.SliceColors[0] = SceneColor.Parse("#ff7f0e");
        diagram.AddSlice("Calcium", 42.96);
        diagram.AddSlice("Potassium", 50.05);
        diagram.AddSlice("Magnesium", 10.01);
        diagram.AddSlice("Iron", 5);
        DiagramAssert.Svg(diagram);
    }

    [Theory]
    [InlineData(PieLegendPosition.Top)]
    [InlineData(PieLegendPosition.Bottom)]
    [InlineData(PieLegendPosition.Center)]
    public void LegendPositions(PieLegendPosition position)
    {
        var diagram = new PieDiagram { LegendPosition = position, Theme = DiagramTheme.Dark };
        for (var i = 0; i < 13; i++)
        {
            diagram.AddSlice("Slice " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), i + 1);
        }

        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void SmallSlicesAreInTheLegendOnly()
    {
        var diagram = new PieDiagram { Theme = DiagramTheme.Forest };
        diagram.AddSlice("Big", 1000);
        diagram.AddSlice("Tiny", 1);
        diagram.AddSlice("Zero", 0);
        diagram.AddSlice("Big", 5);

        var svg = diagram.ToSvg();
        Assert.Equal(1, CountOccurrences(svg, "class=\"pieCircle\""));
        Assert.Contains(">Tiny<", svg, StringComparison.Ordinal);
        Assert.Contains(">100%<", svg, StringComparison.Ordinal);

        // A label keeps its first value, as in Mermaid
        Assert.Equal(1, CountOccurrences(svg, ">Big<"));
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void EmptyAndZeroPies()
    {
        DiagramAssert.Svg(new PieDiagram());

        var zero = new PieDiagram { DonutHole = 0.5, Theme = DiagramTheme.Neutral };
        zero.AddSlice("A", 0);
        zero.AddSlice("B", 0);
        var svg = zero.ToSvg();
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("pieCircle\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeOfItsOwnDerivesTheColors()
    {
        var diagram = new PieDiagram { Theme = DiagramTheme.Default with { PrimaryColor = SceneColor.Parse("#ff0000") } };
        diagram.AddSlice("A", 1);
        diagram.AddSlice("B", 1);
        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"#ff0000\"", svg, StringComparison.Ordinal);
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
        Assert.Equal(expected, Rendering.PieDiagramRenderer.FormatNumber(value));
    }

    [Fact]
    public void ValuesAreValid()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieSlice("a", -1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieSlice("a", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieSlice("a", 1) { Value = double.PositiveInfinity });
        Assert.Throws<ArgumentNullException>("label", () => new PieSlice(null!, 1));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieDiagram { DonutHole = 0.95 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieDiagram { TextPosition = 1.5 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new PieDiagram { Opacity = 2 });
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
