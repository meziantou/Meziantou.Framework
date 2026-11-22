using System.Globalization;
using Meziantou.Framework.Graphviz.Internals;

namespace Meziantou.Framework.Graphviz;

/// <summary>Writes flowcharts in the DOT language of Graphviz, which <see cref="GraphvizDocument.Parse(string, GraphvizParseOptions?)"/> reads back.</summary>
/// <remarks>
/// The graph is a <c>digraph</c>; the subgraphs of the flowchart are clusters. The shapes of the flowcharts that Graphviz
/// does not have are written as the closest shape of Graphviz, the styles of the classes are written on each element, and
/// the theme (except its background), the curves of the edges other than straight and orthogonal ones, the markdown of the
/// labels and the accessibility texts are not written.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Graphviz.Tests/GraphvizWriterTests.cs" ref:region="Graphviz_Write" ref:dedent="true">
/// var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
/// var start = flowchart.AddNode("start", "Start", FlowchartNodeShape.Stadium);
/// var end = flowchart.AddNode("end_node", "End", FlowchartNodeShape.DoubleCircle);
/// flowchart.AddEdge(start, end, "done").LineStyle = FlowchartLineStyle.Dotted;
///
/// var dot = GraphvizWriter.Write(flowchart);
/// </code>
/// </example>
public static class GraphvizWriter
{
    /// <summary>Writes a flowchart in the DOT language.</summary>
    /// <param name="diagram">The flowchart.</param>
    /// <returns>The DOT text, with line feeds.</returns>
    /// <exception cref="ArgumentException">An identifier cannot be written in DOT: it ends with an odd number of backslashes and has unbalanced angle brackets.</exception>
    public static string Write(FlowchartDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        Write(diagram, writer);
        return writer.ToString();
    }

    /// <summary>Writes a flowchart in the DOT language.</summary>
    /// <param name="diagram">The flowchart.</param>
    /// <param name="writer">The writer the text is written to, with line feeds.</param>
    /// <exception cref="ArgumentException">An identifier cannot be written in DOT: it ends with an odd number of backslashes and has unbalanced angle brackets.</exception>
    public static void Write(FlowchartDiagram diagram, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(DotWriter.Write(diagram));
    }
}
