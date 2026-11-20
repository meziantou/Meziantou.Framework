namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>The shapes of the nodes and the arrowheads of Graphviz, and the closest shapes of the flowcharts.</summary>
internal static class DotShapes
{
    /// <summary>Gets the shape of a flowchart closest to a shape of Graphviz, or <see langword="null"/> when the shape is not a shape of Graphviz.</summary>
    public static FlowchartNodeShape? FromDot(string shape, bool rounded, string? sides)
    {
        switch (shape.ToLowerInvariant())
        {
            case "box" or "rect" or "rectangle" or "square" or "msquare":
                return rounded ? FlowchartNodeShape.RoundedRectangle : FlowchartNodeShape.Rectangle;
            case "record":
                return rounded ? FlowchartNodeShape.RoundedRectangle : FlowchartNodeShape.Rectangle;
            case "mrecord":
                return FlowchartNodeShape.RoundedRectangle;
            case "ellipse" or "oval" or "egg":
                return FlowchartNodeShape.Stadium;
            case "circle" or "mcircle":
                return FlowchartNodeShape.Circle;
            case "doublecircle":
                return FlowchartNodeShape.DoubleCircle;
            case "point":
                return FlowchartNodeShape.FilledCircle;
            case "diamond" or "mdiamond":
                return FlowchartNodeShape.Diamond;
            case "hexagon" or "pentagon" or "septagon" or "octagon" or "doubleoctagon" or "tripleoctagon":
                return FlowchartNodeShape.Hexagon;
            case "parallelogram":
                return FlowchartNodeShape.LeanRight;
            case "trapezium":
                return FlowchartNodeShape.TrapezoidBaseBottom;
            case "invtrapezium":
                return FlowchartNodeShape.TrapezoidBaseTop;
            case "triangle":
                return FlowchartNodeShape.Triangle;
            case "invtriangle":
                return FlowchartNodeShape.FlippedTriangle;
            case "house":
                return FlowchartNodeShape.TrapezoidalPentagon;
            case "invhouse":
                return FlowchartNodeShape.Rectangle;
            case "cylinder":
                return FlowchartNodeShape.Cylinder;
            case "note":
                return FlowchartNodeShape.TaggedRectangle;
            case "tab" or "folder":
                return FlowchartNodeShape.Folder;
            case "box3d":
                return FlowchartNodeShape.StackedRectangle;
            case "component":
                return FlowchartNodeShape.LinedRectangle;
            case "plaintext" or "plain" or "none" or "underline":
                return FlowchartNodeShape.Text;
            case "star":
                return FlowchartNodeShape.Bang;
            case "cds" or "rarrow" or "larrow" or "rpromoter" or "lpromoter":
                return FlowchartNodeShape.Odd;
            case "polygon":
                return sides is not null && DotValues.TryParseNumber(sides, out var count) ? count switch
                {
                    3 => FlowchartNodeShape.Triangle,
                    >= 5 => FlowchartNodeShape.Hexagon,
                    _ => FlowchartNodeShape.Rectangle,
                } : FlowchartNodeShape.Rectangle;
            case "promoter" or "terminator" or "utr" or "primersite" or "restrictionsite" or "fivepoverhang" or "threepoverhang" or "noverhang"
                or "assembly" or "signature" or "insulator" or "ribosite" or "rnastab" or "proteasesite" or "proteinstab":
                return FlowchartNodeShape.Rectangle;
            default:
                return null;
        }
    }

    /// <summary>Gets the shape of Graphviz closest to a shape of a flowchart. Reading it back gives a shape that is written the same way.</summary>
    public static string ToDot(FlowchartNodeShape shape) => shape switch
    {
        FlowchartNodeShape.Stadium or FlowchartNodeShape.Cloud => "ellipse",
        FlowchartNodeShape.Cylinder or FlowchartNodeShape.Bucket or FlowchartNodeShape.HorizontalCylinder or FlowchartNodeShape.LinedCylinder => "cylinder",
        FlowchartNodeShape.Folder => "folder",
        FlowchartNodeShape.Circle or FlowchartNodeShape.CrossedCircle => "circle",
        FlowchartNodeShape.DoubleCircle or FlowchartNodeShape.FramedCircle => "doublecircle",
        FlowchartNodeShape.FilledCircle or FlowchartNodeShape.SmallCircle => "point",
        FlowchartNodeShape.Bang => "star",
        FlowchartNodeShape.Diamond => "diamond",
        FlowchartNodeShape.Hexagon => "hexagon",
        FlowchartNodeShape.LeanRight or FlowchartNodeShape.LeanLeft => "parallelogram",
        FlowchartNodeShape.TrapezoidBaseBottom => "trapezium",
        FlowchartNodeShape.TrapezoidBaseTop => "invtrapezium",
        FlowchartNodeShape.Triangle => "triangle",
        FlowchartNodeShape.FlippedTriangle => "invtriangle",
        FlowchartNodeShape.TrapezoidalPentagon => "house",
        FlowchartNodeShape.Text or FlowchartNodeShape.CurlyBraceLeft or FlowchartNodeShape.CurlyBraceRight or FlowchartNodeShape.CurlyBraces => "plaintext",
        FlowchartNodeShape.LinedRectangle => "component",
        FlowchartNodeShape.TaggedRectangle or FlowchartNodeShape.TaggedDocument or FlowchartNodeShape.Document or FlowchartNodeShape.LinedDocument => "note",
        FlowchartNodeShape.StackedRectangle or FlowchartNodeShape.StackedDocument => "box3d",
        FlowchartNodeShape.Odd => "cds",
        _ => "box",
    };

    /// <summary>Gets the decoration of a flowchart closest to an arrowhead of Graphviz, and whether it is the same shape.</summary>
    public static (FlowchartArrowHead ArrowHead, bool IsExact) ArrowFromDot(string arrow) => arrow.ToLowerInvariant() switch
    {
        "none" => (FlowchartArrowHead.None, true),
        "normal" or "onormal" or "vee" or "ovee" or "empty" or "open" or "halfopen" or "lnormal" or "rnormal" or "olnormal" or "ornormal" => (FlowchartArrowHead.Arrow, true),
        "dot" or "odot" => (FlowchartArrowHead.Circle, true),
        "tee" or "otee" => (FlowchartArrowHead.Cross, false),
        _ => (FlowchartArrowHead.Arrow, false),
    };

    public static string ArrowToDot(FlowchartArrowHead arrowHead) => arrowHead switch
    {
        FlowchartArrowHead.None => "none",
        FlowchartArrowHead.Circle => "dot",
        FlowchartArrowHead.Cross => "tee",
        _ => "normal",
    };
}
