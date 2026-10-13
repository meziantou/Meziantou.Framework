namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>The numeric space in which the paints of a drawing are combined with the pixels.</summary>
/// <remarks>See <see cref="DrawingCanvas.Compositing(CompositingSpace, DrawingAction)"/>.</remarks>
public enum CompositingSpace
{
    /// <summary>
    /// Combines the stored (encoded, gamma-compressed) sample values, with exact integer arithmetic. This is the
    /// default, and what CSS, SVG and most image editors do.
    /// </summary>
    Encoded = 0,

    /// <summary>
    /// Converts the samples of the paint and of the pixels from sRGB to linear light, combines them, and converts the
    /// result back: the edge between two colors has the brightness the eye expects (half way from black to white is
    /// the gray 188, not 128), and a thin light line on a dark background keeps its weight. The pixels are assumed to
    /// be sRGB: an image whose ICC profile is not known to be sRGB-compatible is rejected, as no ICC transform is
    /// applied implicitly. An image whose <see cref="Metadata.ImageMetadata.TransferFunction"/> is
    /// <see cref="Metadata.ColorTransferFunction.Linear"/> already stores linear light: its samples are combined as
    /// they are.
    /// </summary>
    LinearSrgb = 1,
}
