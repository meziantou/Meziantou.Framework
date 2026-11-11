namespace Meziantou.Framework.VectorGraphics;

/// <summary>What a primitive of a <see cref="SceneFilterGraph"/> reads beyond the edges of its input, as the SVG <c>edgeMode</c> attribute.</summary>
public enum SceneFilterEdgeMode
{
    /// <summary>Transparent pixels (<c>none</c>).</summary>
    None,

    /// <summary>The pixels of the edge, repeated outward (<c>duplicate</c>).</summary>
    Duplicate,

    /// <summary>The pixels of the opposite edge, as if the input were tiled (<c>wrap</c>).</summary>
    Wrap,

    /// <summary>The pixels of the input, mirrored at its edge (<c>mirror</c>).</summary>
    Mirror,
}
