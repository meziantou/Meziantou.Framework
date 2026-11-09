namespace Meziantou.Framework.Diagrams.Tests;

public sealed class UserJourneyDiagramTests
{
    [Fact]
    public void UserJourney()
    {
        #region Diagrams_UserJourney
        var diagram = new UserJourneyDiagram { Title = "My working day" };
        var work = diagram.AddSection("Go to work");
        work.AddTask("Make tea", 5, "Me");
        work.AddTask("Go upstairs", 3, "Me");
        work.AddTask("Do work", 1, "Me", "Cat");
        var home = diagram.AddSection("Go home");
        home.AddTask("Go downstairs", 5, "Me");
        home.AddTask("Sit down", 5, "Me");
        #endregion

        Assert.Equal(["Cat", "Me"], diagram.GetActors());
        DiagramAssert.Svg(diagram, "UserJourney");
    }

    [Fact]
    public void SectionsColorsAndActors()
    {
        var diagram = new UserJourneyDiagram { Theme = DiagramTheme.Forest };
        for (var i = 0; i < 9; i++)
        {
            var section = diagram.AddSection("Section " + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            section.AddTask("A task with a long name that wraps", i - 2, "Actor " + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "A very long actor name that is wider than the legend allows, so it wraps");
        }

        diagram.AddSection("Empty");
        var svg = diagram.ToSvg();
        Assert.Contains("Actor 8", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void SectionFillsTurnTheThemeColors()
    {
        var fills = Rendering.UserJourneyDiagramRenderer.GetSectionFills(DiagramTheme.Default);
        Assert.Equal(DiagramTheme.Default.PrimaryColor, fills[0]);
        Assert.Equal(DiagramTheme.Default.SecondaryColor, fills[1]);

        // Mermaid: hsl(304, 100%, 96.27%) for the default theme
        Assert.Equal(SceneColor.FromHsl(304, 1, 0.962745098039), fills[2]);
    }
}
