namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>What a mask keeps of the drawing it masks.</summary>
/// <remarks>See <see cref="DrawingCanvas.Mask(DrawingAction, DrawingAction, MaskMode)"/>.</remarks>
public enum MaskMode
{
    /// <summary>The drawing is kept in proportion to the alpha of the mask, whatever its colors: opaque keeps, transparent removes. This is the default.</summary>
    Alpha = 0,

    /// <summary>
    /// The drawing is kept in proportion to the luminance of the mask multiplied by its alpha: white keeps, black and
    /// transparent remove. This is the default of the masks of SVG. The luminance is the Rec. 709 luma of the stored
    /// sample values, the gray level a conversion to grayscale gives.
    /// </summary>
    Luminance = 1,
}
