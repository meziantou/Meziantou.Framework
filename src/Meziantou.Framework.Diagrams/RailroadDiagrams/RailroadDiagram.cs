using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// A railroad diagram, also called a syntax diagram: the rules of a grammar, each drawn as a track that goes through
/// terminals and nonterminals, with branches for the choices and loops for the repetitions (Mermaid
/// <c>railroad-beta</c>, <c>railroad-ebnf-beta</c>, <c>railroad-abnf-beta</c> and <c>railroad-peg-beta</c>).
/// </summary>
public sealed class RailroadDiagram : Diagram
{
    /// <summary>Gets or sets the notation the diagram is written in as Mermaid. It does not change the drawing.</summary>
    public RailroadNotation Notation { get; set; }

    /// <summary>Gets the rules, drawn one below the other.</summary>
    public IList<RailroadRule> Rules { get; } = new List<RailroadRule>();

    /// <summary>Adds a rule.</summary>
    /// <param name="name">The name of the rule.</param>
    /// <param name="definition">The expression the rule stands for.</param>
    /// <returns>The rule.</returns>
    public RailroadRule AddRule(string name, RailroadExpression definition)
    {
        var rule = new RailroadRule(name, definition);
        Rules.Add(rule);
        return rule;
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => RailroadDiagramRenderer.Render(this, context, group);
}
