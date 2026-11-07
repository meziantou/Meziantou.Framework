namespace Meziantou.Framework.Fonts;

/// <summary>
/// How a paint is composed with its backdrop: the composition modes of the COLR table ("CompositeMode enumeration"),
/// which are the Porter-Duff operators and the blend modes of the Compositing and Blending specification of the W3C.
/// </summary>
public enum ColorCompositeMode
{
    /// <summary>Neither the source nor the backdrop is kept.</summary>
    Clear = 0,

    /// <summary>Only the source is kept.</summary>
    Source = 1,

    /// <summary>Only the backdrop is kept.</summary>
    Destination = 2,

    /// <summary>The source is drawn over the backdrop.</summary>
    SourceOver = 3,

    /// <summary>The backdrop is drawn over the source.</summary>
    DestinationOver = 4,

    /// <summary>The part of the source inside the backdrop is kept.</summary>
    SourceIn = 5,

    /// <summary>The part of the backdrop inside the source is kept.</summary>
    DestinationIn = 6,

    /// <summary>The part of the source outside the backdrop is kept.</summary>
    SourceOut = 7,

    /// <summary>The part of the backdrop outside the source is kept.</summary>
    DestinationOut = 8,

    /// <summary>The part of the source inside the backdrop is drawn over the backdrop.</summary>
    SourceAtop = 9,

    /// <summary>The part of the backdrop inside the source is drawn over the source.</summary>
    DestinationAtop = 10,

    /// <summary>The parts of the source and of the backdrop that do not overlap are kept.</summary>
    Xor = 11,

    /// <summary>The source and the backdrop are added.</summary>
    Plus = 12,

    /// <summary>The blend mode screen.</summary>
    Screen = 13,

    /// <summary>The blend mode overlay.</summary>
    Overlay = 14,

    /// <summary>The blend mode darken.</summary>
    Darken = 15,

    /// <summary>The blend mode lighten.</summary>
    Lighten = 16,

    /// <summary>The blend mode color-dodge.</summary>
    ColorDodge = 17,

    /// <summary>The blend mode color-burn.</summary>
    ColorBurn = 18,

    /// <summary>The blend mode hard-light.</summary>
    HardLight = 19,

    /// <summary>The blend mode soft-light.</summary>
    SoftLight = 20,

    /// <summary>The blend mode difference.</summary>
    Difference = 21,

    /// <summary>The blend mode exclusion.</summary>
    Exclusion = 22,

    /// <summary>The blend mode multiply.</summary>
    Multiply = 23,

    /// <summary>The blend mode hue.</summary>
    Hue = 24,

    /// <summary>The blend mode saturation.</summary>
    Saturation = 25,

    /// <summary>The blend mode color.</summary>
    Color = 26,

    /// <summary>The blend mode luminosity.</summary>
    Luminosity = 27,
}
