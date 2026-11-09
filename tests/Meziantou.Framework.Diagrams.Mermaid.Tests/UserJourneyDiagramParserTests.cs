namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class UserJourneyDiagramParserTests
{
    [Fact]
    public void SectionsAndTasks()
    {
        #region Mermaid_UserJourney
        var diagram = (UserJourneyDiagram)MermaidDocument.Parse("""
            journey
                title My working day
                section Go to work
                  Make tea: 5: Me
                  Go upstairs: 3: Me
                  Do work: 1: Me, Cat
                section Go home
                  Go downstairs: 5: Me
                  Sit down: 5: Me
            """).Diagram;
        #endregion

        Assert.Equal("My working day", diagram.Title);
        Assert.Equal(["Go to work", "Go home"], diagram.Sections.Select(section => section.Name));
        var task = diagram.Sections[0].Tasks[2];
        Assert.Equal(("Do work", 1.0, ["Me", "Cat"]), (task.Name, task.Score, task.Actors.ToArray()));
    }

    [Fact]
    public void CommentsSemicolonsAndSectionlessTasks()
    {
        var document = MermaidDocument.Parse("""
            journey
              # A comment
              First: 4.5 # after the score
              Second:: , A ,; Third: 2: B: extra
              SECTION Later %% comment
              Last: -1;%% after a semicolon
            """);
        var diagram = (UserJourneyDiagram)document.Diagram;
        Assert.Equal(string.Empty, diagram.Sections[0].Name);
        Assert.Equal((4.5, 0.0, 2.0), (diagram.Sections[0].Tasks[0].Score, diagram.Sections[0].Tasks[1].Score, diagram.Sections[0].Tasks[2].Score));
        Assert.Equal(["A"], diagram.Sections[0].Tasks[1].Actors);
        Assert.Equal(("Later", -1.0), (diagram.Sections[1].Name, diagram.Sections[1].Tasks[0].Score));
        Assert.Single(document.Warnings);
    }

    [Theory]
    [InlineData("journey\nTask", "A task is written 'name: score: actor, actor'.")]
    [InlineData("journey\nTask:", "The score of the task is expected.")]
    [InlineData("journey\nTask: high: Me", "The score 'high' is not a number.")]
    [InlineData("journey\nsection A: B", "The name of a section cannot contain ':'.")]
    [InlineData("journey\njourney", "'journey' starts the diagram")]
    [InlineData("journey\n: 5", "The name of the task is expected.")]
    public void InvalidUserJourneys(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new UserJourneyDiagram { Title = "A #title", AccessibleTitle = "Journey" };
        diagram.AddSection(string.Empty).AddTask("Before", 2.5);
        var section = diagram.AddSection("Section");
        section.AddTask("Task", 5, "Me", "You");
        section.AddTask("Unhappy", 1e-3);
        diagram.AddSection("Empty");

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (UserJourneyDiagram)parsed.Diagram;
        Assert.Equal(("A #title", "Journey"), (result.Title, result.AccessibleTitle));
        Assert.Equal(["", "Section", "Empty"], result.Sections.Select(item => item.Name));
        Assert.Equal(["Me", "You"], result.Sections[1].Tasks[0].Actors);
        Assert.Equal(1e-3, result.Sections[1].Tasks[1].Score);
    }

    [Theory]
    [InlineData("section A")]
    [InlineData("Title page")]
    [InlineData("a: b")]
    [InlineData("a # b")]
    [InlineData("accTitle")]
    public void WriteRejectsTaskNamesMermaidReadsOtherwise(string name)
    {
        var diagram = new UserJourneyDiagram();
        diagram.AddSection("S").AddTask(name, 1);
        Assert.Throws<ArgumentException>(() => MermaidWriter.Write(diagram));
    }
}
