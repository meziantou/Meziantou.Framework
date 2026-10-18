namespace Meziantou.Framework.Fonts;

/// <summary>
/// How a paint is composed with its backdrop: the composition modes of the COLR table ("CompositeMode enumeration"),
/// which are the Porter-Duff operators and the blend modes of the Compositing and Blending specification of the W3C.
/// </summary>
internal enum ColorCompositeMode
{
    Clear = 0,
    Source = 1,
    Destination = 2,
    SourceOver = 3,
    DestinationOver = 4,
    SourceIn = 5,
    DestinationIn = 6,
    SourceOut = 7,
    DestinationOut = 8,
    SourceAtop = 9,
    DestinationAtop = 10,
    Xor = 11,
    Plus = 12,
    Screen = 13,
    Overlay = 14,
    Darken = 15,
    Lighten = 16,
    ColorDodge = 17,
    ColorBurn = 18,
    HardLight = 19,
    SoftLight = 20,
    Difference = 21,
    Exclusion = 22,
    Multiply = 23,
    Hue = 24,
    Saturation = 25,
    Color = 26,
    Luminosity = 27,
}
