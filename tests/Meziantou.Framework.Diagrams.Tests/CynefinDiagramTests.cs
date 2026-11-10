using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class CynefinDiagramTests
{
    [Fact]
    public void CynefinDiagram()
    {
        #region Diagrams_CynefinDiagram
        var diagram = new CynefinDiagram { Title = "Incident response" };
        diagram.AddItem(CynefinDomain.Complex, "Investigate root cause");
        diagram.AddItem(CynefinDomain.Complex, "Run chaos experiment");
        diagram.AddItem(CynefinDomain.Complicated, "Analyze performance data");
        diagram.AddItem(CynefinDomain.Clear, "Restart service");
        diagram.AddItem(CynefinDomain.Chaotic, "Page on-call immediately");
        diagram.AddItem(CynefinDomain.Confusion, "Unknown failure mode");
        diagram.AddTransition(CynefinDomain.Complex, CynefinDomain.Complicated, "Pattern identified");
        diagram.AddTransition(CynefinDomain.Clear, CynefinDomain.Chaotic, "Complacency");
        #endregion

        Assert.Equal("Restart service", diagram.Items[3].ToString());
        Assert.Equal("Complex --> Complicated: Pattern identified", diagram.Transitions[0].ToString());
        DiagramAssert.Svg(diagram, "CynefinDiagram");
    }

    [Fact]
    public void EmptyFrameworkWithoutDescriptions()
    {
        DiagramAssert.Svg(new CynefinDiagram { ShowDomainDescriptions = false, BoundaryAmplitude = 0, Width = 400, Height = 300 });
    }

    [Fact]
    public void ThemesOverflowAndTransitions()
    {
        var diagram = new CynefinDiagram { Theme = DiagramTheme.Dark, Seed = 42 };
        for (var i = 1; i <= 5; i++)
        {
            diagram.AddItem(CynefinDomain.Confusion, "Unknown " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        diagram.AddItem(CynefinDomain.Complex, "Two\nlines");
        diagram.AddTransition(CynefinDomain.Chaotic, CynefinDomain.Complex, "Stabilized");
        diagram.AddTransition(CynefinDomain.Confusion, CynefinDomain.Clear);
        diagram.AddTransition(CynefinDomain.Clear, CynefinDomain.Clear, "Ignored");
        diagram.Colors.ComplexBackground = SceneColor.Parse("#336699");
        diagram.Colors.ArrowWidth = 4;
        diagram.Colors.BoundaryWidth = 0;
        diagram.Colors.ItemFontSize = 14;
        DiagramAssert.Svg(diagram);
        DiagramAssert.Svg(new CynefinDiagram { Theme = DiagramTheme.Forest });
    }

    [Fact]
    public void WavesAreThoseOfMermaid()
    {
        // The values of the functions of Mermaid in JavaScript
        Assert.Equal(0.6011037519201636, CynefinDiagramRenderer.SeededRandom(42));
        Assert.Equal(0.26642920868471265, CynefinDiagramRenderer.SeededRandom(0));
        Assert.Equal(0.2312965493183583, CynefinDiagramRenderer.SeededRandom(-50));
        Assert.Equal(0.4290980885270983, CynefinDiagramRenderer.SeededRandom(int.MaxValue));
        Assert.Equal(1237376062, CynefinDiagramRenderer.HashString("cynefin"));
        Assert.Equal(1795732973, CynefinDiagramRenderer.HashString("mermaid-1"));
    }

    [Fact]
    public void InvalidValues()
    {
        Assert.Throws<ArgumentNullException>("label", () => new CynefinItem(CynefinDomain.Clear, null!));
        var item = new CynefinItem(CynefinDomain.Clear, "x");
        Assert.Throws<ArgumentNullException>("value", () => item.Label = null!);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new CynefinDiagram { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new CynefinDiagram { BoundaryAmplitude = 51 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new CynefinTheme { CliffWidth = -1 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new CynefinTheme { ItemFontSize = 1 });
    }
}
