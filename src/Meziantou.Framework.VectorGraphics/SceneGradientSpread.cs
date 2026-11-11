namespace Meziantou.Framework.VectorGraphics;

/// <summary>How a gradient paints beyond its ends (<see cref="SceneGradientPaint.Spread"/>), as the <c>spreadMethod</c> of SVG.</summary>
public enum SceneGradientSpread
{
    /// <summary>The colors of the first and the last stops extend beyond the ends.</summary>
    Pad,

    /// <summary>The gradient is repeated, from its start, beyond its ends.</summary>
    Repeat,

    /// <summary>The gradient is repeated beyond its ends, reversed every other time.</summary>
    Reflect,
}
