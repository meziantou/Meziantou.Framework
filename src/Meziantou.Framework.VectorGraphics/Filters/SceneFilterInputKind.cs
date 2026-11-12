namespace Meziantou.Framework.VectorGraphics;

/// <summary>What a primitive of a <see cref="SceneFilterGraph"/> reads (<see cref="SceneFilterInput.Kind"/>).</summary>
public enum SceneFilterInputKind
{
    /// <summary>The result of the primitive before it, or the drawing of the element for the first primitive: the default of SVG, without an <c>in</c> attribute.</summary>
    Previous,

    /// <summary>The drawing of the element (<c>SourceGraphic</c>).</summary>
    SourceGraphic,

    /// <summary>The alpha of the drawing of the element, black where it is opaque (<c>SourceAlpha</c>).</summary>
    SourceAlpha,

    /// <summary>The result of an earlier primitive, by its index in <see cref="SceneFilterGraph.Primitives"/> (<c>result</c>).</summary>
    Result,
}
