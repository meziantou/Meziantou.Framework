using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Mermaid.Internals.ArchitectureDiagrams;
using Meziantou.Framework.Mermaid.Internals.BlockDiagrams;
using Meziantou.Framework.Mermaid.Internals.C4Diagrams;
using Meziantou.Framework.Mermaid.Internals.ClassDiagrams;
using Meziantou.Framework.Mermaid.Internals.CynefinDiagrams;
using Meziantou.Framework.Mermaid.Internals.ErDiagrams;
using Meziantou.Framework.Mermaid.Internals.EventModelingDiagrams;
using Meziantou.Framework.Mermaid.Internals.Flowcharts;
using Meziantou.Framework.Mermaid.Internals.GanttDiagrams;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;
using Meziantou.Framework.Mermaid.Internals.IshikawaDiagrams;
using Meziantou.Framework.Mermaid.Internals.KanbanDiagrams;
using Meziantou.Framework.Mermaid.Internals.MindmapDiagrams;
using Meziantou.Framework.Mermaid.Internals.PacketDiagrams;
using Meziantou.Framework.Mermaid.Internals.PieDiagrams;
using Meziantou.Framework.Mermaid.Internals.RadarDiagrams;
using Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;
using Meziantou.Framework.Mermaid.Internals.RailroadDiagrams;
using Meziantou.Framework.Mermaid.Internals.RequirementDiagrams;
using Meziantou.Framework.Mermaid.Internals.SankeyDiagrams;
using Meziantou.Framework.Mermaid.Internals.SequenceDiagrams;
using Meziantou.Framework.Mermaid.Internals.StateDiagrams;
using Meziantou.Framework.Mermaid.Internals.TimelineDiagrams;
using Meziantou.Framework.Mermaid.Internals.TreemapDiagrams;
using Meziantou.Framework.Mermaid.Internals.TreeViewDiagrams;
using Meziantou.Framework.Mermaid.Internals.UseCaseDiagrams;
using Meziantou.Framework.Mermaid.Internals.UserJourneyDiagrams;
using Meziantou.Framework.Mermaid.Internals.WardleyDiagrams;
using Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;
using Meziantou.Framework.Mermaid.Internals.VennDiagrams;
using Meziantou.Framework.Mermaid.Internals;

namespace Meziantou.Framework.Mermaid;

/// <summary>
/// A diagram read from the Mermaid text format: its content, and the warnings about the valid parts that are not
/// supported. The front matter (<c>title</c>, <c>config</c>), the <c>%%{init: ...}%%</c> directives, the <c>%%</c>
/// comments and the accessibility statements are read for every type of diagram.
/// </summary>
public sealed class MermaidDocument
{
    private MermaidDocument(ISceneSource content, string diagramType, IReadOnlyList<MermaidWarning> warnings)
    {
        Content = content;
        DiagramType = diagramType;
        Warnings = warnings;
    }

    /// <summary>
    /// Gets what the text describes: a <see cref="Diagram"/> of Meziantou.Framework.Diagrams, such as a
    /// <see cref="FlowchartDiagram"/>, or a <see cref="MermaidChart"/> drawn with Meziantou.Framework.Charts, such as a
    /// <see cref="PieChart"/>. Cast it to its type to read or change it.
    /// </summary>
    public ISceneSource Content { get; }

    /// <summary>Gets the keyword that declares the type of the diagram, such as <c>flowchart</c> or <c>classDiagram</c>.</summary>
    public string DiagramType { get; }

    /// <summary>Gets the parts of the text that are valid Mermaid but are not supported, and are ignored.</summary>
    public IReadOnlyList<MermaidWarning> Warnings { get; }

    /// <summary>Gets the keywords of the types of diagrams that can be read.</summary>
    public static IReadOnlyList<string> SupportedDiagramTypes { get; } = ["flowchart", "graph", "flowchart-elk", "swimlane-beta", "classDiagram", "classDiagram-v2", "stateDiagram", "stateDiagram-v2", "erDiagram", "requirementDiagram", "usecase-beta", "C4Context", "C4Container", "C4Component", "C4Dynamic", "C4Deployment", "block", "block-beta", "architecture-beta", "railroad-beta", "railroad-ebnf-beta", "railroad-abnf-beta", "railroad-peg-beta", "sequenceDiagram", "gitGraph", "kanban", "packet", "packet-beta", "gantt", "timeline", "journey", "eventmodeling", "ishikawa-beta", "ishikawa", "mindmap", "treeView-beta", "pie", "radar-beta", "wardley-beta", "xychart", "xychart-beta", "quadrantChart", "cynefin-beta", "treemap", "treemap-beta", "sankey", "sankey-beta", "venn-beta"];

    /// <summary>Reads a Mermaid text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="MermaidParseOptions.Default"/>.</param>
    /// <returns>The document.</returns>
    /// <exception cref="MermaidParseException">The text is not valid, or its type of diagram is not supported.</exception>
    public static MermaidDocument Parse(string text, MermaidParseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= MermaidParseOptions.Default;
        var context = MermaidPreprocessor.Process(text, options);
        var position = 0;
        while (position < context.Text.Length && char.IsWhiteSpace(context.Text[position]))
        {
            position++;
        }

        if (position >= context.Text.Length)
            throw context.Error(position, "The text has no diagram.");

        var end = position;
        while (end < context.Text.Length && (char.IsLetterOrDigit(context.Text[end]) || context.Text[end] is '-' or '_'))
        {
            end++;
        }

        var keyword = context.Text[position..end];
        ISceneSource content = keyword switch
        {
            "flowchart" or "graph" or "flowchart-elk" or "flowchart-v2" or "swimlane-beta" => FlowchartParser.Parse(context, position),
            "classDiagram" or "classDiagram-v2" => ClassDiagramParser.Parse(context, position),
            "stateDiagram" or "stateDiagram-v2" => StateDiagramParser.Parse(context, position),
            "erDiagram" => ErDiagramParser.Parse(context, position),
            "requirementDiagram" => RequirementDiagramParser.Parse(context, position),
            "usecase-beta" => UseCaseDiagramParser.Parse(context, position),
            "C4Context" or "C4Container" or "C4Component" or "C4Dynamic" or "C4Deployment" => C4DiagramParser.Parse(context, position),
            "block" or "block-beta" => BlockDiagramParser.Parse(context, position),
            "architecture-beta" => ArchitectureDiagramParser.Parse(context, position),
            "mindmap" => MindmapDiagramParser.Parse(context, position),
            "railroad-beta" => RailroadDiagramParser.Parse(context, position, RailroadNotation.Primitives),
            "railroad-ebnf-beta" => RailroadDiagramParser.Parse(context, position, RailroadNotation.Ebnf),
            "railroad-abnf-beta" => RailroadDiagramParser.Parse(context, position, RailroadNotation.Abnf),
            "railroad-peg-beta" => RailroadDiagramParser.Parse(context, position, RailroadNotation.Peg),
            "sequenceDiagram" => SequenceDiagramParser.Parse(context, position),
            "gitGraph" => GitGraphDiagramParser.Parse(context, position),
            "kanban" => KanbanDiagramParser.Parse(context, position),
            "packet" or "packet-beta" => PacketDiagramParser.Parse(context, position),
            "gantt" => GanttDiagramParser.Parse(context, position),
            "timeline" => TimelineDiagramParser.Parse(context, position),
            "journey" => UserJourneyDiagramParser.Parse(context, position),
            "eventmodeling" => EventModelingDiagramParser.Parse(context, position),
            _ when IshikawaDiagramParser.GetKeywordLength(keyword, 0, keyword.Length) == keyword.Length => IshikawaDiagramParser.Parse(context, position),
            "treeView-beta" => TreeViewDiagramParser.Parse(context, position),
            "pie" => PieDiagramParser.Parse(context, position),
            "radar-beta" => RadarDiagramParser.Parse(context, position),
            "wardley-beta" => WardleyDiagramParser.Parse(context, position),
            "xychart" or "xychart-beta" => XyChartDiagramParser.Parse(context, position),
            "quadrantChart" => QuadrantChartDiagramParser.Parse(context, position),
            "cynefin-beta" => CynefinDiagramParser.Parse(context, position),
            "treemap" or "treemap-beta" => TreemapDiagramParser.Parse(context, position),
            "sankey" or "sankey-beta" => SankeyDiagramParser.Parse(context, position),
            "venn-beta" => VennDiagramParser.Parse(context, position),
            _ => throw context.Error(position, IsKnownType(keyword) ? $"The diagram type '{keyword}' is not supported yet." : $"No diagram type was found: '{(keyword.Length is 0 ? context.Text[position].ToString() : keyword)}' is not a type of Mermaid diagram."),
        };

        return new MermaidDocument(content, keyword, context.Warnings.ToArray());
    }

    /// <summary>Tries to read a Mermaid text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="document">The document, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the text is valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, [NotNullWhen(true)] out MermaidDocument? document) => TryParse(text, options: null, out document, out _);

    /// <summary>Tries to read a Mermaid text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="options">The options, or <see langword="null"/> for <see cref="MermaidParseOptions.Default"/>.</param>
    /// <param name="document">The document, when the method returns <see langword="true"/>.</param>
    /// <param name="error">The error, when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the text is valid.</returns>
    public static bool TryParse([NotNullWhen(true)] string? text, MermaidParseOptions? options, [NotNullWhen(true)] out MermaidDocument? document, out MermaidParseError error)
    {
        if (text is null)
        {
            document = null;
            error = new MermaidParseError("The text is null.", 1, 1);
            return false;
        }

        try
        {
            document = Parse(text, options);
            error = default;
            return true;
        }
        catch (MermaidParseException exception)
        {
            document = null;
            error = exception.Error;
            return false;
        }
    }

    private static bool IsKnownType(string keyword) => keyword is
        "sequenceDiagram" or "classDiagram" or "classDiagram-v2" or "stateDiagram" or "stateDiagram-v2" or "erDiagram" or "journey" or "gantt" or "pie"
        or "quadrantChart" or "requirementDiagram" or "gitGraph" or "C4Context" or "C4Container" or "C4Component" or "C4Dynamic" or "C4Deployment"
        or "mindmap" or "timeline" or "zenuml" or "sankey" or "sankey-beta" or "xychart" or "xychart-beta" or "block" or "block-beta" or "packet" or "packet-beta"
        or "kanban" or "architecture-beta" or "radar-beta" or "treemap" or "treemap-beta" or "venn-beta" or "ishikawa-beta" or "wardley-beta" or "cynefin-beta"
        or "treeView-beta" or "usecase-beta" or "swimlane-beta" or "railroad-beta" or "railroad-ebnf-beta" or "railroad-abnf-beta" or "railroad-peg-beta" or "eventmodeling" or "agentflow-beta" or "info";
}
