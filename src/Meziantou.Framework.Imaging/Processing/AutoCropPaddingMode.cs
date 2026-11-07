namespace Meziantou.Framework.Imaging;

/// <summary>Selects what an auto-crop does when the padding around the content reaches outside the canvas.</summary>
public enum AutoCropPaddingMode
{
    /// <summary>
    /// Enlarges the canvas: the area outside the original canvas is filled with the detected background color, while the
    /// padding inside the original canvas keeps the original pixels. This is the default.
    /// </summary>
    Expand = 0,

    /// <summary>Clamps the padded rectangle to the original canvas: the result is always a plain crop, and the padding may be smaller on some sides.</summary>
    Contain = 1,
}
