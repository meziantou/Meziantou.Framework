namespace Meziantou.Framework.Diagrams.Mermaid.Tests;

public sealed class StateDiagramParserTests
{
    private static StateDiagram Parse(string text) => (StateDiagram)MermaidDocument.Parse(text).Diagram;

    [Fact]
    public void StatesAndTransitions()
    {
        #region Mermaid_StateDiagram
        var diagram = (StateDiagram)MermaidDocument.Parse("""
            stateDiagram-v2
                [*] --> Still
                Still --> Moving : push
                Moving --> Still
                Moving --> Crash
                Crash --> [*]
            """).Diagram;
        #endregion

        Assert.Equal([StateKind.Start, StateKind.Normal, StateKind.Normal, StateKind.Normal, StateKind.End], diagram.States.Select(state => state.Kind));
        Assert.Equal("push", diagram.Transitions[1].Label?.Text);
        Assert.HasCount(5, diagram.Transitions);
    }

    [Fact]
    public void Declarations()
    {
        var diagram = Parse("""
            stateDiagram
                state "A long description" as s1
                s2 : First line
                s2 : Second line
                state choice <<choice>>
                state fork <<fork>>
                state join <<join>>
                s3:::warm --> s4 %% a comment
                class s1, s2 cold
                style s4 fill:#f00
            """);
        Assert.Equal("A long description", diagram.Find("s1")?.Label?.Text);
        Assert.Equal("First line\nSecond line", diagram.Find("s2")?.Label?.Text);
        Assert.Equal((StateKind.Choice, StateKind.Fork, StateKind.Join), (diagram.Find("choice")!.Kind, diagram.Find("fork")!.Kind, diagram.Find("join")!.Kind));
        Assert.Equal(["warm"], diagram.Find("s3")!.CssClasses);
        Assert.Equal(["cold"], diagram.Find("s1")!.CssClasses);
        Assert.Equal(["cold"], diagram.Find("s2")!.CssClasses);
        Assert.Equal((SceneColor)"#ff0000", diagram.Find("s4")!.Style?.Fill);
    }

    [Fact]
    public void CompositeStatesAndRegions()
    {
        var diagram = Parse("""
            stateDiagram-v2
                [*] --> Active
                state Active {
                    direction LR
                    [*] --> A
                    A --> [*]
                    --
                    [*] --> B
                }
            """);
        var active = diagram.Find("Active")!;
        Assert.Equal(DiagramDirection.LeftToRight, active.Direction);
        Assert.HasCount(2, active.Regions);
        Assert.Equal([StateKind.Start, StateKind.Normal, StateKind.End], active.Regions[0].States.Select(state => state.Kind));
        Assert.Equal([StateKind.Start, StateKind.Normal], active.Regions[1].States.Select(state => state.Kind));

        // Each region has its own start state
        Assert.NotSame(active.Regions[0].States[0], active.Regions[1].States[0]);
        Assert.NotSame(diagram.States[0], active.Regions[0].States[0]);
    }

    [Fact]
    public void Notes()
    {
        var diagram = Parse("""
            stateDiagram-v2
                State1
                note right of State1
                    Important information!
                    On two lines.
                end note
                note left of State1 : A short note
            """);
        Assert.Equal(("Important information!\nOn two lines.", StateNotePosition.Right), (diagram.Notes[0].Text.Text, diagram.Notes[0].Position));
        Assert.Equal(("A short note", StateNotePosition.Left), (diagram.Notes[1].Text.Text, diagram.Notes[1].Position));
    }

    [Theory]
    [InlineData("stateDiagram\nstate A {\nB", "The composite state 'A' is not closed by '}'.")]
    [InlineData("stateDiagram\n}", "(2, 1): '}' closes no composite state.")]
    [InlineData("stateDiagram\n--", "(2, 1): '--' separates the regions of a composite state.")]
    [InlineData("stateDiagram\nstate A <<maybe>>", "(2, 9): 'maybe' is not a kind of state")]
    [InlineData("stateDiagram\nnote over A : x", "(2, 6): 'left of' or 'right of' is expected.")]
    [InlineData("stateDiagram\nnote left of A\ntext", "(2, 1): The note is not closed by 'end note'.")]
    [InlineData("stateDiagram\nA --> ", "(2, 7): Unexpected 'end of text': a state is expected.")]
    public void InvalidStateDiagrams(string text, string expected)
    {
        var exception = Assert.Throws<MermaidParseException>(() => MermaidDocument.Parse(text));
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write()
    {
        var diagram = new StateDiagram { Direction = DiagramDirection.LeftToRight };
        var start = diagram.AddState("s", kind: StateKind.Start);
        var composite = diagram.AddState("Composite", "The composite");
        composite.Direction = DiagramDirection.TopToBottom;
        var inner = composite.AddState("Inner", "First\nSecond");
        var innerStart = composite.AddState("innerStart", kind: StateKind.Start);
        composite.Regions.Add(new StateDiagramRegion());
        var other = composite.AddState("Other");
        var choice = diagram.AddState("Choice", kind: StateKind.Choice);
        diagram.AddTransition(start, composite);
        diagram.AddTransition(innerStart, inner, "go; now");
        diagram.AddTransition(inner, other);
        diagram.AddTransition(composite, choice, "a < b");
        diagram.AddNote(choice, "line 1\nline 2", StateNotePosition.Left);
        diagram.ClassDefinitions["hot"] = new DiagramStyle { Fill = (SceneColor)"#f00" };
        other.CssClasses.Add("hot");
        other.Style = new DiagramStyle { Stroke = SceneColor.Black };

        var text = MermaidWriter.Write(diagram);
        var parsed = MermaidDocument.Parse(text);
        Assert.Equal(text, MermaidWriter.Write(parsed.Diagram));
        var result = (StateDiagram)parsed.Diagram;
        var resultComposite = result.Find("Composite")!;
        Assert.Equal("The composite", resultComposite.Label?.Text);
        Assert.HasCount(2, resultComposite.Regions);
        Assert.Equal("First\nSecond", result.Find("Inner")!.Label?.Text);
        Assert.Equal(StateKind.Start, result.Transitions.Single(transition => transition.Label?.Text == "go; now").From.Kind);
        Assert.Equal("a < b", result.Transitions[^1].Label?.Text);
        Assert.Equal("line 1\nline 2", Assert.Single(result.Notes).Text.Text);
        Assert.Equal(["hot"], result.Find("Other")!.CssClasses);
    }
}
