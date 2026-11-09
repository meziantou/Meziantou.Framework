namespace Meziantou.Framework.Diagrams.Mermaid.Internals.Flowcharts;

/// <summary>The names of the shapes of the <c>@{ shape: ... }</c> syntax: the short names, the aliases and the internal aliases of Mermaid.</summary>
internal static class FlowchartShapes
{
    private static readonly (FlowchartNodeShape Shape, string[] Names)[] Definitions =
    [
        (FlowchartNodeShape.Rectangle, ["rect", "proc", "process", "rectangle", "squareRect", "square"]),
        (FlowchartNodeShape.RoundedRectangle, ["rounded", "event", "roundedRect", "round"]),
        (FlowchartNodeShape.Stadium, ["stadium", "terminal", "pill"]),
        (FlowchartNodeShape.FramedRectangle, ["fr-rect", "subprocess", "subproc", "framed-rectangle", "subroutine"]),
        (FlowchartNodeShape.Cylinder, ["cyl", "db", "database", "cylinder"]),
        (FlowchartNodeShape.DataStore, ["datastore", "data-store"]),
        (FlowchartNodeShape.Folder, ["folder", "directory"]),
        (FlowchartNodeShape.Bucket, ["bucket"]),
        (FlowchartNodeShape.Console, ["console"]),
        (FlowchartNodeShape.Browser, ["browser"]),
        (FlowchartNodeShape.Person, ["person"]),
        (FlowchartNodeShape.Circle, ["circle", "circ"]),
        (FlowchartNodeShape.Bang, ["bang"]),
        (FlowchartNodeShape.Cloud, ["cloud"]),
        (FlowchartNodeShape.Diamond, ["diam", "decision", "diamond", "question"]),
        (FlowchartNodeShape.Hexagon, ["hex", "hexagon", "prepare"]),
        (FlowchartNodeShape.LeanRight, ["lean-r", "lean-right", "in-out", "lean_right"]),
        (FlowchartNodeShape.LeanLeft, ["lean-l", "lean-left", "out-in", "lean_left"]),
        (FlowchartNodeShape.TrapezoidBaseBottom, ["trap-b", "priority", "trapezoid-bottom", "trapezoid"]),
        (FlowchartNodeShape.TrapezoidBaseTop, ["trap-t", "manual", "trapezoid-top", "inv-trapezoid", "inv_trapezoid"]),
        (FlowchartNodeShape.DoubleCircle, ["dbl-circ", "double-circle", "doublecircle"]),
        (FlowchartNodeShape.Text, ["text"]),
        (FlowchartNodeShape.NotchedRectangle, ["notch-rect", "card", "notched-rectangle"]),
        (FlowchartNodeShape.LinedRectangle, ["lin-rect", "lined-rectangle", "lined-process", "lin-proc", "shaded-process"]),
        (FlowchartNodeShape.SmallCircle, ["sm-circ", "start", "small-circle", "stateStart"]),
        (FlowchartNodeShape.FramedCircle, ["fr-circ", "stop", "framed-circle", "stateEnd"]),
        (FlowchartNodeShape.Fork, ["fork", "join", "forkJoin"]),
        (FlowchartNodeShape.Hourglass, ["hourglass", "collate"]),
        (FlowchartNodeShape.CurlyBraceLeft, ["brace", "comment", "brace-l"]),
        (FlowchartNodeShape.CurlyBraceRight, ["brace-r"]),
        (FlowchartNodeShape.CurlyBraces, ["braces"]),
        (FlowchartNodeShape.LightningBolt, ["bolt", "com-link", "lightning-bolt"]),
        (FlowchartNodeShape.Document, ["doc", "document"]),
        (FlowchartNodeShape.HalfRoundedRectangle, ["delay", "half-rounded-rectangle"]),
        (FlowchartNodeShape.HorizontalCylinder, ["h-cyl", "das", "horizontal-cylinder"]),
        (FlowchartNodeShape.LinedCylinder, ["lin-cyl", "disk", "lined-cylinder"]),
        (FlowchartNodeShape.CurvedTrapezoid, ["curv-trap", "curved-trapezoid", "display"]),
        (FlowchartNodeShape.DividedRectangle, ["div-rect", "div-proc", "divided-rectangle", "divided-process"]),
        (FlowchartNodeShape.Triangle, ["tri", "extract", "triangle"]),
        (FlowchartNodeShape.WindowPane, ["win-pane", "internal-storage", "window-pane"]),
        (FlowchartNodeShape.FilledCircle, ["f-circ", "junction", "filled-circle"]),
        (FlowchartNodeShape.TrapezoidalPentagon, ["notch-pent", "loop-limit", "notched-pentagon"]),
        (FlowchartNodeShape.FlippedTriangle, ["flip-tri", "manual-file", "flipped-triangle"]),
        (FlowchartNodeShape.SlopedRectangle, ["sl-rect", "manual-input", "sloped-rectangle"]),
        (FlowchartNodeShape.StackedDocument, ["docs", "documents", "st-doc", "stacked-document"]),
        (FlowchartNodeShape.StackedRectangle, ["st-rect", "procs", "processes", "stacked-rectangle"]),
        (FlowchartNodeShape.BowTieRectangle, ["bow-rect", "stored-data", "bow-tie-rectangle"]),
        (FlowchartNodeShape.CrossedCircle, ["cross-circ", "summary", "crossed-circle"]),
        (FlowchartNodeShape.TaggedDocument, ["tag-doc", "tagged-document"]),
        (FlowchartNodeShape.TaggedRectangle, ["tag-rect", "tagged-rectangle", "tag-proc", "tagged-process"]),
        (FlowchartNodeShape.Flag, ["flag", "paper-tape"]),
        (FlowchartNodeShape.Odd, ["odd", "rect_left_inv_arrow"]),
        (FlowchartNodeShape.LinedDocument, ["lin-doc", "lined-document"]),
    ];

    private static readonly Dictionary<string, FlowchartNodeShape> ByName = CreateByName();

    public static bool TryGetShape(string name, out FlowchartNodeShape shape) => ByName.TryGetValue(name, out shape);

    public static string GetShortName(FlowchartNodeShape shape)
    {
        foreach (var (definitionShape, names) in Definitions)
        {
            if (definitionShape == shape)
                return names[0];
        }

        return "rect";
    }

    private static Dictionary<string, FlowchartNodeShape> CreateByName()
    {
        var result = new Dictionary<string, FlowchartNodeShape>(StringComparer.Ordinal);
        foreach (var (shape, names) in Definitions)
        {
            foreach (var name in names)
            {
                result.TryAdd(name, shape);
            }
        }

        return result;
    }
}
