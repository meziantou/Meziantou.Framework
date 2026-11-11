namespace Meziantou.Framework.VectorGraphics;

/// <summary>Whether a <see cref="SceneMorphologyPrimitive"/> makes the shapes thinner or thicker: the <c>operator</c> of <c>feMorphology</c>.</summary>
public enum SceneMorphologyOperator
{
    /// <summary>Thinner: each component is the smallest one around the pixel (<c>erode</c>).</summary>
    Erode,

    /// <summary>Thicker: each component is the largest one around the pixel (<c>dilate</c>).</summary>
    Dilate,
}
