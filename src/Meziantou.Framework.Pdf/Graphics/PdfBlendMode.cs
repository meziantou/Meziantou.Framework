namespace Meziantou.Framework.Pdf;

/// <summary>The blend modes of the transparent imaging model (ISO 32000-2, 11.3.5).</summary>
public enum PdfBlendMode
{
    /// <summary><c>Normal</c>: the source color.</summary>
    Normal,

    /// <summary><c>Multiply</c>: the product of the backdrop and source colors.</summary>
    Multiply,

    /// <summary><c>Screen</c>: the complement of the product of the complements.</summary>
    Screen,

    /// <summary><c>Overlay</c>: <c>Multiply</c> or <c>Screen</c>, depending on the backdrop color.</summary>
    Overlay,

    /// <summary><c>Darken</c>: the darker of the backdrop and source colors.</summary>
    Darken,

    /// <summary><c>Lighten</c>: the lighter of the backdrop and source colors.</summary>
    Lighten,

    /// <summary><c>ColorDodge</c>: the backdrop color brightened to reflect the source color.</summary>
    ColorDodge,

    /// <summary><c>ColorBurn</c>: the backdrop color darkened to reflect the source color.</summary>
    ColorBurn,

    /// <summary><c>HardLight</c>: <c>Multiply</c> or <c>Screen</c>, depending on the source color.</summary>
    HardLight,

    /// <summary><c>SoftLight</c>: darkens or lightens the colors, depending on the source color.</summary>
    SoftLight,

    /// <summary><c>Difference</c>: the darker color subtracted from the lighter one.</summary>
    Difference,

    /// <summary><c>Exclusion</c>: <c>Difference</c> with a lower contrast.</summary>
    Exclusion,

    /// <summary><c>Hue</c>: the hue of the source color, the saturation and luminosity of the backdrop.</summary>
    Hue,

    /// <summary><c>Saturation</c>: the saturation of the source color, the hue and luminosity of the backdrop.</summary>
    Saturation,

    /// <summary><c>Color</c>: the hue and saturation of the source color, the luminosity of the backdrop.</summary>
    Color,

    /// <summary><c>Luminosity</c>: the luminosity of the source color, the hue and saturation of the backdrop.</summary>
    Luminosity,
}
