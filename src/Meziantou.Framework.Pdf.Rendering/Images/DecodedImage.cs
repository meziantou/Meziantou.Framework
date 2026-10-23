using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.Pdf.Rendering.Images;

/// <summary>An image of a document decoded to sRGB with its alpha: the image, or the alpha of a stencil mask.</summary>
internal sealed class DecodedImage(Image<Rgba32> image, bool isStencil, bool interpolate)
{
    public Image<Rgba32> Image { get; } = image;

    public int Width => Image.Width;

    public int Height => Image.Height;

    /// <summary>Gets whether the image is a stencil mask (8.9.6.2): its alpha says where the fill color is painted.</summary>
    public bool IsStencil { get; } = isStencil;

    public bool Interpolate { get; } = interpolate;
}
