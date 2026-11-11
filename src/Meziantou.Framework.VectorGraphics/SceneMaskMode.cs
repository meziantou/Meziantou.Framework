namespace Meziantou.Framework.VectorGraphics;

/// <summary>What the content of a mask gives the opacity by (<see cref="SceneMask.Mode"/>).</summary>
public enum SceneMaskMode
{
    /// <summary>The luminance of the colors times their alpha: white shows the element, black hides it.</summary>
    Luminance,

    /// <summary>The alpha of the colors: what is drawn shows the element, whatever its color.</summary>
    Alpha,
}
