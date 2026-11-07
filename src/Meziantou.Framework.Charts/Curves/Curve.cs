namespace Meziantou.Framework.Charts;

/// <summary>How the points of a line or an area are joined.</summary>
public enum Curve
{
    /// <summary>Straight segments.</summary>
    Linear,

    /// <summary>Horizontal steps that change halfway between two points.</summary>
    Step,

    /// <summary>Horizontal steps that change at the start of each point.</summary>
    StepBefore,

    /// <summary>Horizontal steps that change at the end of each point.</summary>
    StepAfter,

    /// <summary>A smooth curve that preserves the monotonicity of the data along x (Fritsch–Carlson), as d3's <c>curveMonotoneX</c>: it does not overshoot.</summary>
    MonotoneX,

    /// <summary>A smooth centripetal Catmull–Rom spline (alpha 0.5) that passes through every point.</summary>
    CatmullRom,
}
