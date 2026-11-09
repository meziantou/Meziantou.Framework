using System.Text;
using Meziantou.Framework.Diagrams.Mermaid.Internals.ArchitectureDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.BlockDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.C4Diagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.ClassDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.CynefinDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.ErDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.EventModelingDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.Flowcharts;
using Meziantou.Framework.Diagrams.Mermaid.Internals.GanttDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.GitGraphDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.IshikawaDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.KanbanDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.MindmapDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.PacketDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.PieDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.RadarDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.QuadrantChartDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.RailroadDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.RequirementDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.SankeyDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.SequenceDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.StateDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.TimelineDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.TreemapDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.TreeViewDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.UseCaseDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.UserJourneyDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.WardleyDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.XyChartDiagrams;
using Meziantou.Framework.Diagrams.Mermaid.Internals.VennDiagrams;

namespace Meziantou.Framework.Diagrams.Mermaid;

/// <summary>Writes diagrams as Mermaid text, which <see cref="MermaidDocument.Parse(string, MermaidParseOptions?)"/> reads back.</summary>
/// <remarks>
/// The theme is written when it is one of the themes of Mermaid (<see cref="DiagramTheme.Dark"/>...); a modified theme is
/// not written. The text measurer is not written.
/// </remarks>
public static class MermaidWriter
{
    /// <summary>Writes a diagram as Mermaid text.</summary>
    /// <param name="diagram">The diagram.</param>
    /// <returns>The Mermaid text, with line feeds.</returns>
    /// <exception cref="ArgumentException">The diagram has an element that Mermaid cannot express, such as an identifier with spaces.</exception>
    /// <exception cref="NotSupportedException">The type of the diagram cannot be written.</exception>
    public static string Write(Diagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var builder = new StringBuilder();
        switch (diagram)
        {
            case FlowchartDiagram flowchart:
                FlowchartWriter.Write(builder, flowchart);
                break;
            case ClassDiagram classDiagram:
                ClassDiagramWriter.Write(builder, classDiagram);
                break;
            case StateDiagram stateDiagram:
                StateDiagramWriter.Write(builder, stateDiagram);
                break;
            case ErDiagram erDiagram:
                ErDiagramWriter.Write(builder, erDiagram);
                break;
            case RequirementDiagram requirementDiagram:
                RequirementDiagramWriter.Write(builder, requirementDiagram);
                break;
            case UseCaseDiagram useCaseDiagram:
                UseCaseDiagramWriter.Write(builder, useCaseDiagram);
                break;
            case C4Diagram c4Diagram:
                C4DiagramWriter.Write(builder, c4Diagram);
                break;
            case BlockDiagram blockDiagram:
                BlockDiagramWriter.Write(builder, blockDiagram);
                break;
            case ArchitectureDiagram architectureDiagram:
                ArchitectureDiagramWriter.Write(builder, architectureDiagram);
                break;
            case RailroadDiagram railroadDiagram:
                RailroadDiagramWriter.Write(builder, railroadDiagram);
                break;
            case SequenceDiagram sequenceDiagram:
                SequenceDiagramWriter.Write(builder, sequenceDiagram);
                break;
            case GitGraphDiagram gitGraph:
                GitGraphDiagramWriter.Write(builder, gitGraph);
                break;
            case KanbanDiagram kanbanDiagram:
                KanbanDiagramWriter.Write(builder, kanbanDiagram);
                break;
            case PacketDiagram packetDiagram:
                PacketDiagramWriter.Write(builder, packetDiagram);
                break;
            case GanttDiagram ganttDiagram:
                GanttDiagramWriter.Write(builder, ganttDiagram);
                break;
            case TimelineDiagram timelineDiagram:
                TimelineDiagramWriter.Write(builder, timelineDiagram);
                break;
            case UserJourneyDiagram userJourneyDiagram:
                UserJourneyDiagramWriter.Write(builder, userJourneyDiagram);
                break;
            case EventModelingDiagram eventModelingDiagram:
                EventModelingDiagramWriter.Write(builder, eventModelingDiagram);
                break;
            case IshikawaDiagram ishikawaDiagram:
                IshikawaDiagramWriter.Write(builder, ishikawaDiagram);
                break;
            case XyChartDiagram xyChartDiagram:
                XyChartDiagramWriter.Write(builder, xyChartDiagram);
                break;
            case MindmapDiagram mindmapDiagram:
                MindmapDiagramWriter.Write(builder, mindmapDiagram);
                break;
            case TreeViewDiagram treeViewDiagram:
                TreeViewDiagramWriter.Write(builder, treeViewDiagram);
                break;
            case PieDiagram pieDiagram:
                PieDiagramWriter.Write(builder, pieDiagram);
                break;
            case RadarDiagram radarDiagram:
                RadarDiagramWriter.Write(builder, radarDiagram);
                break;
            case WardleyDiagram wardleyDiagram:
                WardleyDiagramWriter.Write(builder, wardleyDiagram);
                break;
            case QuadrantChartDiagram quadrantChartDiagram:
                QuadrantChartDiagramWriter.Write(builder, quadrantChartDiagram);
                break;
            case CynefinDiagram cynefinDiagram:
                CynefinDiagramWriter.Write(builder, cynefinDiagram);
                break;
            case TreemapDiagram treemapDiagram:
                TreemapDiagramWriter.Write(builder, treemapDiagram);
                break;
            case SankeyDiagram sankeyDiagram:
                SankeyDiagramWriter.Write(builder, sankeyDiagram);
                break;
            case VennDiagram vennDiagram:
                VennDiagramWriter.Write(builder, vennDiagram);
                break;
            default:
                throw new NotSupportedException($"The diagram '{diagram.GetType().Name}' cannot be written as Mermaid.");
        }

        return builder.ToString();
    }
}
