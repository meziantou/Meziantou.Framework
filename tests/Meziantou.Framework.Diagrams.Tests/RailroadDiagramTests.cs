namespace Meziantou.Framework.Diagrams.Tests;

public sealed class RailroadDiagramTests
{
    [Fact]
    public void RailroadDiagram()
    {
        #region Diagrams_RailroadDiagram
        var diagram = new RailroadDiagram { Title = "Numbers" };
        diagram.AddRule("number", RailroadExpression.Sequence(
            RailroadExpression.Optional(RailroadExpression.Choice(RailroadExpression.Terminal("+"), RailroadExpression.Terminal("-"))),
            RailroadExpression.OneOrMore(RailroadExpression.Nonterminal("digit")),
            RailroadExpression.Optional(RailroadExpression.Sequence(RailroadExpression.Terminal("."), RailroadExpression.ZeroOrMore(RailroadExpression.Nonterminal("digit"))))));
        diagram.AddRule("digit", RailroadExpression.Special("a digit from 0 to 9"));
        #endregion

        DiagramAssert.Svg(diagram, "RailroadDiagram");
    }

    [Fact]
    public void RepetitionBounds()
    {
        var diagram = new RailroadDiagram();
        diagram.AddRule("bounded", RailroadExpression.Sequence(
            new RailroadRepetition(RailroadExpression.Terminal("a"), 2, 4),
            new RailroadRepetition(RailroadExpression.Terminal("b"), 3, 3),
            new RailroadRepetition(RailroadExpression.Terminal("c"), 2),
            new RailroadRepetition(RailroadExpression.Terminal("d"), 0, 1),
            new RailroadRepetition(RailroadExpression.Terminal("e"), 1, 1)));
        diagram.AddRule("empty", RailroadExpression.Sequence());
        var svg = diagram.ToSvg();
        Assert.Contains(">2..4<", svg, StringComparison.Ordinal);
        Assert.Contains(">2+<", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg);
    }

    [Fact]
    public void Validation()
    {
        Assert.Throws<ArgumentException>("name", () => new RailroadRule(string.Empty, RailroadExpression.Terminal("x")));
        Assert.Throws<ArgumentOutOfRangeException>("minimum", () => new RailroadRepetition(RailroadExpression.Terminal("x"), -1));
        Assert.Throws<ArgumentOutOfRangeException>("maximum", () => new RailroadRepetition(RailroadExpression.Terminal("x"), 0, -1));
        Assert.Throws<ArgumentNullException>("items", () => new RailroadSequence(RailroadExpression.Terminal("x"), null!));
    }
}
