namespace Meziantou.Framework.Diagrams;

/// <summary>The shape of a node of a flowchart: the shapes of Mermaid.</summary>
public enum FlowchartNodeShape
{
    /// <summary>A rectangle: a process (Mermaid <c>rect</c>, <c>A[text]</c>).</summary>
    Rectangle,

    /// <summary>A rectangle with rounded corners: an event (Mermaid <c>rounded</c>, <c>A(text)</c>).</summary>
    RoundedRectangle,

    /// <summary>A rectangle with round ends: a terminal point (Mermaid <c>stadium</c>, <c>A([text])</c>).</summary>
    Stadium,

    /// <summary>A rectangle with vertical lines on its sides: a subprocess (Mermaid <c>fr-rect</c>, <c>A[[text]]</c>).</summary>
    FramedRectangle,

    /// <summary>A vertical cylinder: a database (Mermaid <c>cyl</c>, <c>A[(text)]</c>).</summary>
    Cylinder,

    /// <summary>Two horizontal lines: a data store (Mermaid <c>datastore</c>).</summary>
    DataStore,

    /// <summary>A folder (Mermaid <c>folder</c>).</summary>
    Folder,

    /// <summary>A bucket (Mermaid <c>bucket</c>).</summary>
    Bucket,

    /// <summary>A terminal window (Mermaid <c>console</c>).</summary>
    Console,

    /// <summary>A browser window (Mermaid <c>browser</c>).</summary>
    Browser,

    /// <summary>A person: a head above a rounded rectangle (Mermaid <c>person</c>).</summary>
    Person,

    /// <summary>A circle: a start (Mermaid <c>circle</c>, <c>A((text))</c>).</summary>
    Circle,

    /// <summary>An explosion (Mermaid <c>bang</c>).</summary>
    Bang,

    /// <summary>A cloud (Mermaid <c>cloud</c>).</summary>
    Cloud,

    /// <summary>A diamond: a decision (Mermaid <c>diam</c>, <c>A{text}</c>).</summary>
    Diamond,

    /// <summary>A hexagon: a preparation (Mermaid <c>hex</c>, <c>A{{text}}</c>).</summary>
    Hexagon,

    /// <summary>A parallelogram leaning right: an input or an output (Mermaid <c>lean-r</c>, <c>A[/text/]</c>).</summary>
    LeanRight,

    /// <summary>A parallelogram leaning left: an output or an input (Mermaid <c>lean-l</c>, <c>A[\text\]</c>).</summary>
    LeanLeft,

    /// <summary>A trapezoid with its long base at the bottom: a priority action (Mermaid <c>trap-b</c>, <c>A[/text\]</c>).</summary>
    TrapezoidBaseBottom,

    /// <summary>A trapezoid with its long base at the top: a manual operation (Mermaid <c>trap-t</c>, <c>A[\text/]</c>).</summary>
    TrapezoidBaseTop,

    /// <summary>Two concentric circles: a stop (Mermaid <c>dbl-circ</c>, <c>A(((text)))</c>).</summary>
    DoubleCircle,

    /// <summary>A text without a border (Mermaid <c>text</c>).</summary>
    Text,

    /// <summary>A rectangle with a cut corner: a card (Mermaid <c>notch-rect</c>).</summary>
    NotchedRectangle,

    /// <summary>A rectangle with a vertical line on its left: a lined or shaded process (Mermaid <c>lin-rect</c>).</summary>
    LinedRectangle,

    /// <summary>A small filled circle: a start (Mermaid <c>sm-circ</c>). It has no label.</summary>
    SmallCircle,

    /// <summary>A small filled circle in a ring: a stop (Mermaid <c>fr-circ</c>). It has no label.</summary>
    FramedCircle,

    /// <summary>A filled bar: a fork or a join (Mermaid <c>fork</c>). It has no label.</summary>
    Fork,

    /// <summary>An hourglass: a collate (Mermaid <c>hourglass</c>).</summary>
    Hourglass,

    /// <summary>A text with a curly brace on its left: a comment (Mermaid <c>brace</c>).</summary>
    CurlyBraceLeft,

    /// <summary>A text with a curly brace on its right: a comment (Mermaid <c>brace-r</c>).</summary>
    CurlyBraceRight,

    /// <summary>A text between curly braces: a comment (Mermaid <c>braces</c>).</summary>
    CurlyBraces,

    /// <summary>A lightning bolt: a communication link (Mermaid <c>bolt</c>).</summary>
    LightningBolt,

    /// <summary>A rectangle with a wavy bottom: a document (Mermaid <c>doc</c>).</summary>
    Document,

    /// <summary>A rectangle with a round right side: a delay (Mermaid <c>delay</c>).</summary>
    HalfRoundedRectangle,

    /// <summary>A horizontal cylinder: a direct access storage (Mermaid <c>h-cyl</c>).</summary>
    HorizontalCylinder,

    /// <summary>A cylinder with a line under its top: a disk storage (Mermaid <c>lin-cyl</c>).</summary>
    LinedCylinder,

    /// <summary>A display (Mermaid <c>curv-trap</c>).</summary>
    CurvedTrapezoid,

    /// <summary>A rectangle divided by a line under its top: a divided process (Mermaid <c>div-rect</c>).</summary>
    DividedRectangle,

    /// <summary>A triangle pointing up: an extract (Mermaid <c>tri</c>).</summary>
    Triangle,

    /// <summary>A rectangle with a line under its top and on its left: an internal storage (Mermaid <c>win-pane</c>).</summary>
    WindowPane,

    /// <summary>A small filled circle: a junction (Mermaid <c>f-circ</c>). It has no label.</summary>
    FilledCircle,

    /// <summary>A rectangle with cut top corners: a loop limit (Mermaid <c>notch-pent</c>).</summary>
    TrapezoidalPentagon,

    /// <summary>A triangle pointing down: a manual file (Mermaid <c>flip-tri</c>).</summary>
    FlippedTriangle,

    /// <summary>A rectangle with a sloped top: a manual input (Mermaid <c>sl-rect</c>).</summary>
    SlopedRectangle,

    /// <summary>A stack of documents (Mermaid <c>docs</c>).</summary>
    StackedDocument,

    /// <summary>A stack of rectangles: a multi-process (Mermaid <c>st-rect</c>).</summary>
    StackedRectangle,

    /// <summary>A rectangle with curved sides: stored data (Mermaid <c>bow-rect</c>).</summary>
    BowTieRectangle,

    /// <summary>A circle with a cross: a summary (Mermaid <c>cross-circ</c>).</summary>
    CrossedCircle,

    /// <summary>A document with a folded corner: a tagged document (Mermaid <c>tag-doc</c>).</summary>
    TaggedDocument,

    /// <summary>A rectangle with a folded corner: a tagged process (Mermaid <c>tag-rect</c>).</summary>
    TaggedRectangle,

    /// <summary>A rectangle with wavy top and bottom: a paper tape (Mermaid <c>flag</c>).</summary>
    Flag,

    /// <summary>A rectangle with a notch on its left (Mermaid <c>odd</c>, <c>A&gt;text]</c>).</summary>
    Odd,

    /// <summary>A document with a vertical line on its left (Mermaid <c>lin-doc</c>).</summary>
    LinedDocument,
}
