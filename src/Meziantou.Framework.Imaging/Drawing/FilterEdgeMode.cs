namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>What a filter reads beyond the edges of what it filters: the pixels a blur or an offset reaches there.</summary>
public enum FilterEdgeMode
{
    /// <summary>
    /// Transparent pixels: what is filtered fades at its edges (<c>edgeMode="none"</c> of SVG). This is the default. A
    /// filter of a drawing reads what is drawn beyond the canvas, within its reach, before the transparent pixels.
    /// </summary>
    Transparent = 0,

    /// <summary>The pixels of the edges, extended (<c>edgeMode="duplicate"</c> of SVG).</summary>
    Duplicate = 1,

    /// <summary>The pixels of the opposite edge: what is filtered repeats (<c>edgeMode="wrap"</c> of SVG).</summary>
    Wrap = 2,

    /// <summary>The pixels mirrored at the edge, the pixel of the edge repeated once: what <c>backdrop-filter</c> of CSS reads around the backdrop.</summary>
    Mirror = 3,
}
