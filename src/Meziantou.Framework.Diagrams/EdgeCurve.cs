namespace Meziantou.Framework.Diagrams;

/// <summary>How the line of an edge goes through the points the layout gives it, as the curves of d3 that Mermaid uses.</summary>
public enum EdgeCurve
{
    /// <summary>A B-spline: a smooth curve that does not go through the inner points (Mermaid <c>basis</c>, the default).</summary>
    Basis,

    /// <summary>Straight segments (Mermaid <c>linear</c>).</summary>
    Linear,

    /// <summary>Horizontal and vertical segments, the vertical step at the middle of each segment (Mermaid <c>step</c>).</summary>
    Step,

    /// <summary>Horizontal and vertical segments, the vertical step at the start of each segment (Mermaid <c>stepBefore</c>).</summary>
    StepBefore,

    /// <summary>Horizontal and vertical segments, the vertical step at the end of each segment (Mermaid <c>stepAfter</c>).</summary>
    StepAfter,

    /// <summary>A cardinal spline through every point (Mermaid <c>cardinal</c>).</summary>
    Cardinal,

    /// <summary>A centripetal Catmull–Rom spline through every point (Mermaid <c>catmullRom</c>).</summary>
    CatmullRom,

    /// <summary>A curve through every point that keeps the monotonicity in x (Mermaid <c>monotoneX</c>).</summary>
    MonotoneX,

    /// <summary>A curve through every point that keeps the monotonicity in y (Mermaid <c>monotoneY</c>).</summary>
    MonotoneY,

    /// <summary>A natural cubic spline through every point (Mermaid <c>natural</c>).</summary>
    Natural,

    /// <summary>Cubic curves between the points, with horizontal tangents (Mermaid <c>bumpX</c>).</summary>
    BumpX,

    /// <summary>Cubic curves between the points, with vertical tangents (Mermaid <c>bumpY</c>).</summary>
    BumpY,
}
