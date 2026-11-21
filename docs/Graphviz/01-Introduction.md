---
title: Introduction
description: What the Graphviz library reads and writes, and how it relates to the diagram library.
---

`Meziantou.Framework.Graphviz` reads the DOT language of [Graphviz](https://graphviz.org) and writes it back. A DOT
graph becomes a [FlowchartDiagram](cref:Meziantou.Framework.Diagrams.FlowchartDiagram) that you can read, change and
draw as SVG or as an image, without installing Graphviz.

## What you can do with it

- **Read DOT text**: directed and undirected graphs, strict graphs, the default attributes of the nodes and the edges,
  the subgraphs and the clusters, the edges between groups of nodes (`a -> {b c}`), the shapes, the styles, the colors,
  and the escapes of the labels. See [getting started](02-GettingStarted.md).
- **Report the problems** of a text written by a user: the parser gives the line and the column of an error, and lists
  the valid features it cannot draw as warnings.
- **Write DOT text** from a flowchart built in C#, or read from [Mermaid](../Mermaid/01-Introduction.md), to draw it
  with Graphviz or to give it to another tool that reads DOT.

## How the graphs are drawn

The flowcharts are laid out by the layered layout of the diagram library, a port of the layout of dagre, which follows
the `dot` engine of Graphviz: the nodes are placed in ranks in the direction of the edges, and the edges are routed
between them. A graph looks close to what `dot` draws, but not identical: the library measures and draws its own texts,
and uses the shapes and the themes of the flowcharts.

Some parts of DOT have no equivalent in the flowcharts. The parser reads them, approximates or ignores them, and
reports them as warnings: the other layout engines (`neato`, `fdp`, `circo`, `twopi`), the ports of the nodes, the
fields of the record shapes, the tables of the HTML-like labels, most arrowheads and the ranks of the subgraphs
(`rank=same`). See [the DOT language](03-DotLanguage.md) for what is supported.

## Namespaces

The reader and the writer are in the [Meziantou.Framework.Graphviz](cref:Meziantou.Framework.Graphviz) namespace, and
the flowcharts in [Meziantou.Framework.Diagrams](cref:Meziantou.Framework.Diagrams).

## Next steps

- [Getting started](02-GettingStarted.md): install the package, read a graph, draw it and write it back.
- [The DOT language](03-DotLanguage.md): the statements, the attributes and the shapes the parser reads.
- [Flowcharts](../Diagrams/04-DiagramTypes/02-Flowcharts.md): the diagrams the graphs become.
