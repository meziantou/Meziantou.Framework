namespace Meziantou.Framework.VectorGraphics;

/// <summary>The noise of a <see cref="SceneTurbulencePrimitive"/>: the <c>type</c> of <c>feTurbulence</c>.</summary>
public enum SceneTurbulenceType
{
    /// <summary>The sum of the absolute values of the octaves (<c>turbulence</c>), the default of SVG.</summary>
    Turbulence,

    /// <summary>The sum of the octaves (<c>fractalNoise</c>).</summary>
    FractalNoise,
}
