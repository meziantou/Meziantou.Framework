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
    /// background keeps its weight. The function is the one <see cref="ResizeWorkingSpace.LinearSrgb"/> uses: sRGB,
    /// unless the image has a CICP color description (<see cref="Metadata.ImageMetadata.Cicp"/>) and no ICC profile,
    /// whose transfer characteristics then give it. An image whose ICC profile is not known to be sRGB-compatible is
    /// rejected, as no ICC transform is applied implicitly, and so are reserved transfer characteristics. An image
    /// whose <see cref="Metadata.ImageMetadata.TransferFunction"/> is
    /// <see cref="Metadata.ColorTransferFunction.Linear"/>, or whose transfer characteristics are linear, already
    /// stores linear light: its samples are combined as they are.
    /// </summary>
    /// <remarks>The name is the one of the other working spaces: the space is the linear light of the image's own transfer function.</remarks>
    LinearSrgb = 1,
}
