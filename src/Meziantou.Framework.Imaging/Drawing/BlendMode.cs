namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>How the color of a paint is mixed with the color of the pixels it is drawn on, before it is composited: the blend modes of image editors and of CSS.</summary>
/// <remarks>
/// <para>
/// With the colors of the paint <c>s</c> and of the pixel <c>d</c> as fractions of one, a blend mode gives a color
/// <c>B(s, d)</c>. The paint takes the color <c>(1 - ad) s + ad B(s, d)</c>, where <c>ad</c> is the alpha of the pixel:
/// on a transparent pixel the paint keeps its color, and on an opaque one it has the blended color. It is then
/// composited by the <see cref="CompositeMode"/> of the brush, with its own alpha, as any paint is. The formulas are
/// those of "Compositing and Blending Level 1" of the W3C.
/// </para>
/// <para>
/// The blended color is computed in double precision from the sample values, and rounded to the nearest sample value,
/// ties upward, before the paint is composited with the exact arithmetic of the operators. Only additions,
/// multiplications, divisions and square roots are involved: the pixels are the same on every platform. The modes
/// from <see cref="Multiply"/> to <see cref="Exclusion"/> mix each color channel on its own; the last four take a
/// color as a whole, with the luminosity <c>0.3 R + 0.59 G + 0.11 B</c>. On a grayscale frame the paint and the pixels
/// are gray levels, which these four modes treat as colors without saturation.
/// </para>
/// </remarks>
public enum BlendMode
{
    /// <summary>The color of the paint, as it is. This is the default.</summary>
    Normal = 0,

    /// <summary>The product of the two colors, <c>s d</c>: always darker, as two slides held together. White changes nothing.</summary>
    Multiply = 1,

    /// <summary>The inverse of the product of the inverses, <c>s + d - s d</c>: always lighter, as two projectors on one screen. Black changes nothing.</summary>
    Screen = 2,

    /// <summary><see cref="Multiply"/> where the pixel is dark and <see cref="Screen"/> where it is light: <see cref="HardLight"/> with the two colors exchanged.</summary>
    Overlay = 3,

    /// <summary>The darker of the two colors, channel by channel.</summary>
    Darken = 4,

    /// <summary>The lighter of the two colors, channel by channel.</summary>
    Lighten = 5,

    /// <summary>The pixel brightened to reflect the paint: <c>min(1, d / (1 - s))</c>. Black changes nothing.</summary>
    ColorDodge = 6,

    /// <summary>The pixel darkened to reflect the paint: <c>1 - min(1, (1 - d) / s)</c>. White changes nothing.</summary>
    ColorBurn = 7,

    /// <summary><see cref="Multiply"/> where the paint is dark and <see cref="Screen"/> where it is light: a harsh spotlight.</summary>
    HardLight = 8,

    /// <summary>The pixel darkened where the paint is dark and lightened where it is light, without reaching black or white: a diffused spotlight.</summary>
    SoftLight = 9,

    /// <summary>The difference of the two colors, <c>|d - s|</c>. White inverts the pixel; black changes nothing.</summary>
    Difference = 10,

    /// <summary><c>s + d - 2 s d</c>: like <see cref="Difference"/>, with less contrast.</summary>
    Exclusion = 11,

    /// <summary>The hue of the paint, with the saturation and the luminosity of the pixel.</summary>
    Hue = 12,

    /// <summary>The saturation of the paint, with the hue and the luminosity of the pixel.</summary>
    Saturation = 13,

    /// <summary>The hue and the saturation of the paint, with the luminosity of the pixel: tints a gray image.</summary>
    Color = 14,

    /// <summary>The luminosity of the paint, with the hue and the saturation of the pixel.</summary>
    Luminosity = 15,
}
