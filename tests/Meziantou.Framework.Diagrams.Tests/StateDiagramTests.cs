namespace Meziantou.Framework.Diagrams.Tests;

public sealed class StateDiagramTests
{
    [Fact]
    public void StateDiagram()
    {
        #region Diagrams_StateDiagram
        var diagram = new StateDiagram();
        var start = diagram.AddState("start", kind: StateKind.Start);
        var draft = diagram.AddState("Draft");
        var review = diagram.AddState("Review");
        var published = diagram.AddState("Published");
        var end = diagram.AddState("end", kind: StateKind.End);

        diagram.AddTransition(start, draft);
        diagram.AddTransition(draft, review, "submit");
        diagram.AddTransition(review, draft, "reject");
        diagram.AddTransition(review, published, "approve");
        diagram.AddTransition(published, end);
        diagram.AddNote(review, "Two approvals are required");
        #endregion

        DiagramAssert.Svg(diagram, "StateDiagram");
    }

    [Fact]
    public void CompositeStates()
    {
        #region Diagrams_StateDiagramComposite
        var diagram = new StateDiagram();
        var active = diagram.AddState("Active");

        // A composite state with two concurrent regions
        var numLockOff = active.AddState("NumLockOff");
        var numLockOn = active.AddState("NumLockOn");
        active.Regions.Add(new StateDiagramRegion());
        var capsLockOff = active.AddState("CapsLockOff");
        var capsLockOn = active.AddState("CapsLockOn");

        diagram.AddTransition(numLockOff, numLockOn, "press");
        diagram.AddTransition(numLockOn, numLockOff, "press");
        diagram.AddTransition(capsLockOff, capsLockOn, "press");
        diagram.AddTransition(capsLockOn, capsLockOff, "press");
        #endregion

        Assert.True(active.IsComposite);
        Assert.HasCount(2, active.Regions);
        DiagramAssert.Svg(diagram, "StateDiagramComposite");
    }

    [Fact]
    public void ChoicesForksAndJoins()
    {
        var diagram = new StateDiagram { Direction = DiagramDirection.LeftToRight };
        var choice = diagram.AddState("choice", kind: StateKind.Choice);
        var fork = diagram.AddState("fork", kind: StateKind.Fork);
        var join = diagram.AddState("join", kind: StateKind.Join);
        var a = diagram.AddState("A");
        var b = diagram.AddState("B");
        diagram.AddTransition(choice, fork, "yes");
        diagram.AddTransition(fork, a);
        diagram.AddTransition(fork, b);
        diagram.AddTransition(a, join);
        diagram.AddTransition(b, join);
        diagram.AddNote(join, "Both done", StateNotePosition.Left);
        Assert.Same(join, diagram.Find("join"));
        Assert.Equal(["choice", "fork", "join", "A", "B"], diagram.EnumerateStates().Select(state => state.Id));
        DiagramAssert.Svg(diagram);
    }

    [Fact]
    public void Styles()
    {
        var diagram = new StateDiagram();
        diagram.ClassDefinitions["bad"] = new DiagramStyle { Fill = (SceneColor)"#ff0000", TextColor = SceneColor.White };
        var crash = diagram.AddState("Crash");
        crash.CssClasses.Add("bad");
        var svg = diagram.ToSvg();
        Assert.Contains("fill=\"#ff0000\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"state bad\"", svg, StringComparison.Ordinal);
    }
}
