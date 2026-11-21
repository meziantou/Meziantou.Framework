using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Graphviz.Internals;

namespace Meziantou.Framework.Graphviz;

/// <summary>
/// A graph read from the DOT language of <see href="https://graphviz.org">Graphviz</see>, drawn as a
/// <see cref="FlowchartDiagram"/>: the nodes, the edges and the clusters, with their shapes, styles, colors and labels.
/// The layout is the layered layout of the flowcharts, close to the <c>dot</c> engine of Graphviz.
/// </summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Graphviz.Tests/GraphvizDocumentTests.cs" ref:region="Graphviz_Parse" ref:dedent="true">
/// var text = """
///     digraph Orders {
///         rankdir=LR;
///         node [shape=box, style=rounded];
///         start [label="Order received", shape=ellipse];
///         check [label="Payment valid?", shape=diamond];
///         subgraph cluster_fulfillment {
///             label="Fulfillment";
///             ship [label="Ship the order"];
///             notify [label="Notify the customer"];
///         }
///         cancel [label="Cancel the order", color=red];
///         start -&gt; check;
///         check -&gt; ship [label="yes"];
///         ship -&gt; notify;
///         check -&gt; cancel [label="no", style=dashed];
///     }
///     """;
///
/// var document = GraphvizDocument.Parse(text);
/// var flowchart = document.Diagram;
/// var svg = flowchart.ToSvg();
/// </code>
/// </example>
public sealed class GraphvizDocument
{
    private GraphvizDocument(FlowchartDiagram diagram, string? name, bool isDirected, bool isStrict, IReadOnlyList<GraphvizWarning> warnings)
    {
        Diagram = diagram;
        Name = name;
        IsDirected = isDirected;
        IsStrict = isStrict;
        Warnings = warnings;
    }

    /// <summary>Gets the flowchart of the graph. The clusters (the subgraphs whose name starts with <c>cluster</c>) are its subgraphs.</summary>
    public FlowchartDiagram Diagram { get; }

    /// <summary>Gets the name of the graph (<c>digraph name { }</c>), or <see langword="null"/> when it has none.</summary>
    public string? Name { get; }

    /// <summary>Gets a value that indicates whether the graph is directed (<c>digraph</c>). The edges of an undirected graph have no arrowheads.</summary>
    public bool IsDirected { get; }

    /// <summary>Gets a value that indicates whether the graph is strict: the edges between the same nodes are merged.</summary>
    public bool IsStrict { get; }

    /// <summary>Gets the parts of the text that are valid DOT but are not supported, and are ignored or approximated.</summary>
    public IReadOnlyList<GraphvizWarning> Warnings { get; }

    /// <summary>Reads a DOT text. Only its first graph is read.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="GraphvizParseOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="GraphvizParseException">The text is not valid, or it exceeds a limit of the options.</exception>
    public static GraphvizDocument Parse(string text, GraphvizParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= GraphvizParseOptions.Default;
        if (text.Length > options.MaxTextSize)
            throw new GraphvizParseException(new GraphvizParseError(string.Create(CultureInfo.InvariantCulture, $"The text is too long: the limit is {options.MaxTextSize} characters."), 1, 1));

        var context = new DotContext(text, options);
        var graph = DotParser.Parse(context);
        var diagram = DotFlowchartBuilder.Build(context, graph);
        return new GraphvizDocument(diagram, graph.Name, graph.IsDirected, graph.IsStrict, context.Warnings.ToArray());
    }

    /// <summary>Tries to read a DOT text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="document">The document, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out GraphvizDocument? document) => TryParse(text, options: null, out document, out _);

    /// <summary>Tries to read a DOT text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="GraphvizParseOptions.Default"/>.</param>
    /// <param name="document">The document, when the method returns <see langword="true"/>.</param>
    /// <param name="error">The error, when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text is valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, GraphvizParseOptions? options, [NotNullWhen(true)] out GraphvizDocument? document, out GraphvizParseError error)
    {
        if (text is null)
        {
            document = null;
            error = new GraphvizParseError("The text is null.", 1, 1);
            return false;
        }

        try
        {
            document = Parse(text, options);
            error = default;
            return true;
        }
        catch (GraphvizParseException exception)
        {
            document = null;
            error = exception.Error;
            return false;
        }
    }
}
