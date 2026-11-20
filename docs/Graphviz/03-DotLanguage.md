---
title: The DOT language
description: The statements, attributes, shapes, arrowheads, colors and labels of DOT that the parser reads, and how they are drawn.
---

## Statements

The parser reads the [grammar of DOT](https://graphviz.org/doc/info/lang.html):

- `graph` and `digraph`, `strict` (the edges between the same nodes are merged), and the name of the graph.
- The node statements (`a [label="A"]`), the edge statements, with chains (`a -> b -> c`) and groups of nodes
  (`a -> {b c}`, `subgraph s {d e} -> f`), and the attribute statements (`graph [...]`, `node [...]`, `edge [...]`,
  `name=value`).
- The subgraphs, anonymous or named. A subgraph whose name starts with `cluster`, or that has `cluster=true`, is a
  cluster: it becomes a subgraph of the flowchart, drawn as a frame. The other subgraphs only group nodes and
  attributes. A node of several clusters is drawn in the innermost one.
- The identifiers, the numbers, the quoted strings joined with `+`, the lines continued with a backslash, and the HTML
  strings (`<...>`). The keywords are case-insensitive.
- The comments (`//`, `/* */`) and the lines of the C preprocessor (`#`).

As in Graphviz, the default attributes set by `node [...]` and `edge [...]` apply to the nodes and the edges created
after them, in the same subgraph and its nested subgraphs. A subgraph starts with the attributes its parent graph has
when the subgraph is created: a `label` set at the top of the graph is also the title of the clusters that follow, so
set it after them.

The edges of a `digraph` are written `->`, and the edges of a `graph` `--`. The edges of an undirected graph have no
arrowheads, unless their `dir` attribute adds some. The ports of the nodes (`a:port:n`) are read and ignored: the edges
are attached to the nodes.

## Attributes

| Element | Attribute | Effect |
|---|---|---|
| Graph | `label` | The [Title](cref:Meziantou.Framework.Diagrams.Diagram.Title) of the diagram. |
| Graph | `rankdir` | The [Direction](cref:Meziantou.Framework.Diagrams.FlowchartDiagram.Direction): `TB`, `LR`, `BT` or `RL`. |
| Graph | `nodesep`, `ranksep` | The [NodeSpacing](cref:Meziantou.Framework.Diagrams.FlowchartDiagram.NodeSpacing) and the [RankSpacing](cref:Meziantou.Framework.Diagrams.FlowchartDiagram.RankSpacing), in inches (72 pixels). |
| Graph | `splines` | `line`, `polyline` and `false` draw straight edges, `ortho` draws horizontal and vertical segments, `spline` and `true` curves. |
| Graph | `bgcolor` | The background of the theme. |
| Graph | `compound` | Enables `lhead` and `ltail`. |
| Cluster | `label` | The title of the frame. A cluster without a label has no title. |
| Cluster | `bgcolor`, `fillcolor`, `color`, `pencolor` | The background (`bgcolor`, or `fillcolor` with `style=filled`) and the border (`pencolor`, else `color`). |
| Node | `label` | The label; without one, the node shows its name. |
| Node | `shape`, `sides` | The shape: see the shapes below. |
| Node | `color`, `fillcolor` | The border, and the background of a node with `style=filled` (`fillcolor`, else `color`, else light gray). |
| Edge | `label` | The label. |
| Edge | `dir`, `arrowhead`, `arrowtail` | The arrowheads: see the arrowheads below. |
| Edge | `color` | The color of the line. |
| Edge | `minlen` | The [Length](cref:Meziantou.Framework.Diagrams.FlowchartEdge.Length): the minimum number of ranks the edge spans, from 1 to 100. |
| Edge | `lhead`, `ltail` | With `compound=true`, the edge ends at the frame of a cluster that contains its end: its end is the subgraph. |
| All | `style` | See the styles below. |
| All | `fontcolor`, `fontsize`, `penwidth` | The color and the size of the texts, and the width of the lines. |
| Nodes and clusters | `URL` (or `href`), `tooltip` | The link and the tooltip of the element in the SVG. |
| All | `class` | The CSS classes of the element in the SVG. |

The other attributes, such as `fontname`, `width`, `height`, `weight` or `constraint`, are ignored and reported as
warnings. So is a `layout` other than `dot`, and the `rank` of the subgraphs (`rank=same`): the layered layout places
the nodes.

## Styles

The `style` attribute is a list of styles separated by commas:

- `filled` fills a node or a cluster.
- `rounded` rounds the corners of a box (`shape=box`, `record`).
- `dashed` and `dotted` draw dashed and dotted lines. An edge that is `dashed` or `dotted` is a
  [Dotted](cref:Meziantou.Framework.Diagrams.FlowchartLineStyle.Dotted) edge.
- `bold` draws a [Thick](cref:Meziantou.Framework.Diagrams.FlowchartLineStyle.Thick) edge, and the border of a node 2
  pixels wide. `setlinewidth(n)` sets the width of the lines.
- `invis` hides the element: an [Invisible](cref:Meziantou.Framework.Diagrams.FlowchartLineStyle.Invisible) edge, or a
  node or a cluster whose opacity is 0, which keeps its place in the layout.

The other styles (`diagonals`, `striped`, `wedged`, `radial`, `tapered`) are ignored and reported as warnings.

## Shapes

Each shape of Graphviz is drawn as the closest shape of the flowcharts:

| Graphviz | Flowchart |
|---|---|
| `box`, `rect`, `rectangle`, `square`, `Msquare`, `invhouse`, `record` | [Rectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Rectangle) ([RoundedRectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.RoundedRectangle) with `style=rounded`) |
| `Mrecord` | [RoundedRectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.RoundedRectangle) |
| `ellipse` (the default), `oval`, `egg` | [Stadium](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Stadium) |
| `circle`, `Mcircle` | [Circle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Circle) |
| `doublecircle` | [DoubleCircle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.DoubleCircle) |
| `point` | [FilledCircle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.FilledCircle) |
| `diamond`, `Mdiamond` | [Diamond](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Diamond) |
| `hexagon`, `pentagon`, `septagon`, `octagon`, `doubleoctagon`, `tripleoctagon` | [Hexagon](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Hexagon) |
| `parallelogram` | [LeanRight](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.LeanRight) |
| `trapezium`, `invtrapezium` | [TrapezoidBaseBottom](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.TrapezoidBaseBottom), [TrapezoidBaseTop](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.TrapezoidBaseTop) |
| `triangle`, `invtriangle` | [Triangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Triangle), [FlippedTriangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.FlippedTriangle) |
| `house` | [TrapezoidalPentagon](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.TrapezoidalPentagon) |
| `cylinder` | [Cylinder](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Cylinder) |
| `note` | [TaggedRectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.TaggedRectangle) |
| `folder`, `tab` | [Folder](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Folder) |
| `box3d` | [StackedRectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.StackedRectangle) |
| `component` | [LinedRectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.LinedRectangle) |
| `plaintext`, `plain`, `none`, `underline` | [Text](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Text) |
| `star` | [Bang](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Bang) |
| `cds`, `rarrow`, `larrow`, `rpromoter`, `lpromoter` | [Odd](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Odd) |
| `polygon` | [Triangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Triangle) with `sides=3`, [Hexagon](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Hexagon) with 5 sides or more, else [Rectangle](cref:Meziantou.Framework.Diagrams.FlowchartNodeShape.Rectangle) |

The shapes of synthetic biology (`promoter`, `terminator`...) are drawn as rectangles. A shape Graphviz does not have
is drawn as a rectangle, with a warning.

## Arrowheads

The `dir` attribute chooses the ends of an edge that have an arrowhead: `forward` (the default of a `digraph`), `back`,
`both` or `none` (the default of a `graph`). `arrowhead` and `arrowtail` choose their shape:

| Graphviz | Flowchart |
|---|---|
| `normal`, `vee`, `empty`, `open`, `halfopen` and their `o`, `l` and `r` variants | [Arrow](cref:Meziantou.Framework.Diagrams.FlowchartArrowHead.Arrow) |
| `dot`, `odot` | [Circle](cref:Meziantou.Framework.Diagrams.FlowchartArrowHead.Circle) |
| `tee`, `otee` | [Cross](cref:Meziantou.Framework.Diagrams.FlowchartArrowHead.Cross), with a warning |
| `none` | [None](cref:Meziantou.Framework.Diagrams.FlowchartArrowHead.None) |

The other arrowheads, such as `diamond`, `box` or `crow`, are drawn as arrows, with a warning.

## Colors

A color is a color name of X11, as Graphviz reads it (`green` is `#00ff00`, `gray` is `#bebebe`), or of CSS, `grayN`
from 0 to 100, `#rrggbb` or `#rrggbbaa`, or three numbers from 0 to 1 for a color in HSV (`"0.6 0.5 1"`). A list of
colors (`"red:blue"`) uses its first color, and a scheme other than `x11` (`/accent3/1`) and the numbered colors of
X11 (`red2`) are ignored, with a warning.

## Labels

The labels read the escapes of Graphviz: `\n`, `\l` and `\r` start a new line, `\N` is the name of the node, `\G` the
name of the graph, `\E` the name of the edge (`tail->head`), and `\T` and `\H` the names of its ends. The line break
that ends the last line is removed.

The labels of the `record` and `Mrecord` shapes are drawn as lines: each field is a line, without the separators and
the ports. The HTML-like labels (`label=<...>`) are drawn as plain text: their line breaks and the rows of their
tables start new lines, and their fonts, colors and images are ignored, with a warning.
