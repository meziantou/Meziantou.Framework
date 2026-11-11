namespace Meziantou.Framework.VectorGraphics;

/// <summary>How an element is blended with what is drawn under it (<see cref="SceneElement.BlendMode"/>), as the <c>mix-blend-mode</c> of CSS and the blend modes of PDF.</summary>
public enum SceneBlendMode
{
    /// <summary>The element is drawn over what is under it.</summary>
    Normal,

    /// <summary>The colors are multiplied: the result is darker.</summary>
    Multiply,

    /// <summary>The complements of the colors are multiplied: the result is lighter.</summary>
    Screen,

    /// <summary>Multiply or screen, depending on the color under the element.</summary>
    Overlay,

    /// <summary>The darker of the two colors.</summary>
    Darken,

    /// <summary>The lighter of the two colors.</summary>
    Lighten,

    /// <summary>The color under the element is brightened to reflect the element.</summary>
    ColorDodge,

    /// <summary>The color under the element is darkened to reflect the element.</summary>
    ColorBurn,

    /// <summary>Multiply or screen, depending on the color of the element.</summary>
    HardLight,

    /// <summary>A softer version of hard light.</summary>
    SoftLight,

    /// <summary>The difference of the two colors.</summary>
    Difference,

    /// <summary>A lower-contrast version of difference.</summary>
    Exclusion,

    /// <summary>The hue of the element, with the saturation and the luminosity under it.</summary>
    Hue,

    /// <summary>The saturation of the element, with the hue and the luminosity under it.</summary>
    Saturation,

    /// <summary>The hue and the saturation of the element, with the luminosity under it.</summary>
    Color,

    /// <summary>The luminosity of the element, with the hue and the saturation under it.</summary>
    Luminosity,
}
