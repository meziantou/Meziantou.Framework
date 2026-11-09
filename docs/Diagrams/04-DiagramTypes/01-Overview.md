---
title: Overview
description: The types of diagrams, and the Mermaid keyword of each.
---

| Diagram | Type | Mermaid keyword |
| --- | --- | --- |
| [Flowchart](02-Flowcharts.md) | [FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram) | `flowchart`, `graph` |
| [Swimlanes](03-Swimlanes.md) | [FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram) with [Swimlanes](cref:Meziantou.Framework.Diagrams.FlowchartDiagram.Swimlanes) | `swimlane-beta` |
| [Class diagram](04-ClassDiagrams.md) | [ClassDiagram](cref:Meziantou.Framework.Diagrams.ClassDiagram) | `classDiagram`, `classDiagram-v2` |
| [State diagram](05-StateDiagrams.md) | [StateDiagram](cref:Meziantou.Framework.Diagrams.StateDiagram) | `stateDiagram`, `stateDiagram-v2` |
| [Entity relationship diagram](06-ErDiagrams.md) | [ErDiagram](cref:Meziantou.Framework.Diagrams.ErDiagram) | `erDiagram` |
| [Requirement diagram](07-RequirementDiagrams.md) | [RequirementDiagram](cref:Meziantou.Framework.Diagrams.RequirementDiagram) | `requirementDiagram` |
| [Use case diagram](08-UseCaseDiagrams.md) | [UseCaseDiagram](cref:Meziantou.Framework.Diagrams.UseCaseDiagram) | `usecase-beta` |
| [C4 diagram](09-C4Diagrams.md) | [C4Diagram](cref:Meziantou.Framework.Diagrams.C4Diagram) | `C4Context`, `C4Container`, `C4Component`, `C4Dynamic`, `C4Deployment` |
| [Block diagram](10-BlockDiagrams.md) | [BlockDiagram](cref:Meziantou.Framework.Diagrams.BlockDiagram) | `block`, `block-beta` |
| [Architecture diagram](11-ArchitectureDiagrams.md) | [ArchitectureDiagram](cref:Meziantou.Framework.Diagrams.ArchitectureDiagram) | `architecture-beta` |
| [Sequence diagram](12-SequenceDiagrams.md) | [SequenceDiagram](cref:Meziantou.Framework.Diagrams.SequenceDiagram) | `sequenceDiagram` |
| [Gantt diagram](13-GanttDiagrams.md) | [GanttDiagram](cref:Meziantou.Framework.Diagrams.GanttDiagram) | `gantt` |
| [Timeline](14-Timelines.md) | [TimelineDiagram](cref:Meziantou.Framework.Diagrams.TimelineDiagram) | `timeline` |
| [Git graph](15-GitGraphs.md) | [GitGraphDiagram](cref:Meziantou.Framework.Diagrams.GitGraphDiagram) | `gitGraph` |
| [Kanban diagram](16-KanbanDiagrams.md) | [KanbanDiagram](cref:Meziantou.Framework.Diagrams.KanbanDiagram) | `kanban` |
| [Packet diagram](17-PacketDiagrams.md) | [PacketDiagram](cref:Meziantou.Framework.Diagrams.PacketDiagram) | `packet`, `packet-beta` |
| [User journey](18-UserJourneys.md) | [UserJourneyDiagram](cref:Meziantou.Framework.Diagrams.UserJourneyDiagram) | `journey` |
| [Event modeling](19-EventModeling.md) | [EventModelingDiagram](cref:Meziantou.Framework.Diagrams.EventModelingDiagram) | `eventmodeling` |
| [Railroad diagram](20-RailroadDiagrams.md) | [RailroadDiagram](cref:Meziantou.Framework.Diagrams.RailroadDiagram) | `railroad-beta`, `railroad-ebnf-beta`, `railroad-abnf-beta`, `railroad-peg-beta` |
| [Mindmap](21-Mindmaps.md) | [MindmapDiagram](cref:Meziantou.Framework.Diagrams.MindmapDiagram) | `mindmap` |
| [Tree view](22-TreeViews.md) | [TreeViewDiagram](cref:Meziantou.Framework.Diagrams.TreeViewDiagram) | `treeView-beta` |
| [Ishikawa diagram](23-IshikawaDiagrams.md) | [IshikawaDiagram](cref:Meziantou.Framework.Diagrams.IshikawaDiagram) | `ishikawa-beta`, `ishikawa` |
| [Treemap](24-Treemaps.md) | [TreemapDiagram](cref:Meziantou.Framework.Diagrams.TreemapDiagram) | `treemap`, `treemap-beta` |
| [Pie chart](25-PieCharts.md) | [PieDiagram](cref:Meziantou.Framework.Diagrams.PieDiagram) | `pie` |
| [XY chart](26-XyCharts.md) | [XyChartDiagram](cref:Meziantou.Framework.Diagrams.XyChartDiagram) | `xychart`, `xychart-beta` |
| [Quadrant chart](27-QuadrantCharts.md) | [QuadrantChartDiagram](cref:Meziantou.Framework.Diagrams.QuadrantChartDiagram) | `quadrantChart` |
| [Sankey diagram](28-SankeyDiagrams.md) | [SankeyDiagram](cref:Meziantou.Framework.Diagrams.SankeyDiagram) | `sankey`, `sankey-beta` |
| [Radar chart](29-RadarCharts.md) | [RadarDiagram](cref:Meziantou.Framework.Diagrams.RadarDiagram) | `radar-beta` |
| [Venn diagram](30-VennDiagrams.md) | [VennDiagram](cref:Meziantou.Framework.Diagrams.VennDiagram) | `venn-beta` |
| [Wardley map](31-WardleyMaps.md) | [WardleyDiagram](cref:Meziantou.Framework.Diagrams.WardleyDiagram) | `wardley-beta` |
| [Cynefin diagram](32-CynefinDiagrams.md) | [CynefinDiagram](cref:Meziantou.Framework.Diagrams.CynefinDiagram) | `cynefin-beta` |

Every diagram derives from [Diagram](cref:Meziantou.Framework.Diagrams.Diagram), which gives it a
[Title](cref:Meziantou.Framework.Diagrams.Diagram.Title), an accessible
[Description](cref:Meziantou.Framework.Diagrams.Diagram.Description), a [Theme](cref:Meziantou.Framework.Diagrams.Diagram.Theme)
and the SVG output.
