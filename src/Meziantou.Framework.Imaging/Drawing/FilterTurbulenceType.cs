namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The noise a <see cref="TurbulencePrimitive"/> makes: the <c>type</c> of <c>feTurbulence</c> of SVG.</summary>
public enum FilterTurbulenceType
{
    /// <summary>The sum of the absolute values of the octaves: <c>turbulence</c>, with sharp creases.</summary>
    Turbulence = 0,

    /// <summary>Half of one plus the sum of the octaves: <c>fractalNoise</c>, smooth, around one half.</summary>
    FractalNoise = 1,
}
