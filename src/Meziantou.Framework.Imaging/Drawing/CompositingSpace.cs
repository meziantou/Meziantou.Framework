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
    /// Converts the samples of the paint and of the pixels to linear light with the transfer function of the image,
    /// combines them, and converts the result back: the edge between two colors has the brightness the eye expects
    /// (with sRGB samples, half way from black to white is the gray 188, not 128), and a thin light line on a dark
    /// background keeps its weight. The function is sRGB, unless the image says otherwise: without ICC profile, the
    /// transfer characteristics of its CICP color description (<see cref="Metadata.ImageMetadata.Cicp"/>) give it,
    /// as for <see cref="ResizeWorkingSpace.LinearSrgb"/>; with an ICC profile that is not the one of sRGB, each
    /// channel is decoded and encoded by its tone curve in the profile (the three curves of a matrix-based RGB
    /// profile, the curve of a monochrome one). A profile that describes its colors with lookup tables only has no
    /// such curve and is rejected, and so are reserved transfer characteristics. An image whose
    /// <see cref="Metadata.ImageMetadata.TransferFunction"/> is <see cref="Metadata.ColorTransferFunction.Linear"/>,
    /// or whose transfer characteristics are linear, already stores linear light: its samples are combined as they
    /// are.
    /// </summary>
    /// <remarks>The name is the one of the other working spaces: the space is the linear light of the image's own transfer function.</remarks>
    LinearSrgb = 1,
}
