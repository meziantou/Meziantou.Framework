namespace Meziantou.Framework.Diagrams.Tests;

public sealed class TimelineDiagramTests
{
    [Fact]
    public void TimelineDiagram()
    {
        #region Diagrams_TimelineDiagram
        var diagram = new TimelineDiagram { Title = "History of .NET" };
        var framework = diagram.AddSection(".NET Framework");
        framework.AddPeriod("2002", "Version 1.0");
        framework.AddPeriod("2005", "Generics", "64-bit support");
        framework.AddPeriod("2007", "LINQ");

        var core = diagram.AddSection(".NET Core and .NET");
        core.AddPeriod("2016", ".NET Core 1.0");
        core.AddPeriod("2020", ".NET 5\nOne .NET");
        #endregion

        Assert.Equal(["2002", "2005", "2007"], framework.Periods.Select(period => period.Label.Text));
        Assert.Equal(["Generics", "64-bit support"], framework.Periods[1].Events.Select(item => item.Text));
        DiagramAssert.Svg(diagram, "TimelineDiagram");
    }

    [Fact]
    public void PeriodsWithoutSections()
    {
        var diagram = new TimelineDiagram { Title = "Social media" };
        diagram.AddPeriod("2002", "LinkedIn");
        diagram.AddPeriod("2004", "Facebook", "Google");
        diagram.AddPeriod("2005", "YouTube");
        diagram.AddPeriod("2006");
        var svg = diagram.ToSvg();

        // Without sections, each period has its color of the palette
        Assert.Contains(SvgFill(DiagramTheme.Default.GetPaletteColor(0)), svg, StringComparison.Ordinal);
        Assert.Contains(SvgFill(DiagramTheme.Default.GetPaletteColor(3)), svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);

        diagram.DisableMulticolor = true;
        Assert.DoesNotContain(SvgFill(DiagramTheme.Default.GetPaletteColor(3)), diagram.ToSvg(), StringComparison.Ordinal);
    }

    [Fact]
    public void TopToBottom()
    {
        var diagram = new TimelineDiagram { Direction = TimelineDirection.TopToBottom, Theme = DiagramTheme.Forest };
        var first = diagram.AddSection("2023 Q1\nRelease Personal Tier");
        first.AddPeriod("Bullet 1", "sub-point 1a", "sub-point 1b");
        first.AddPeriod("Bullet 2", "sub-point 2a");
        var second = diagram.AddSection("2023 Q2");
        second.AddPeriod("Bullet 3", "A long event that wraps on several lines because it does not fit in the box");
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void EmptyDiagram()
    {
        var svg = new TimelineDiagram().ToSvg();
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        var empty = new TimelineDiagram();
        empty.AddSection("Empty section");
        Assert.DoesNotContain("NaN", empty.ToSvg(), StringComparison.Ordinal);
    }

    private static string SvgFill(SceneColor color) => "fill=\"" + color.ToString() + "\"";
}
