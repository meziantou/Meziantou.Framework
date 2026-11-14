namespace Meziantou.Framework.Imaging.Typography;

/// <summary>
/// The image a font gives for a glyph (<c>sbix</c>, <c>CBDT</c>, <c>EBDT</c> or <c>bdat</c> tables), decoded at the size
/// of its strike, and where the font places it.
/// </summary>
/// <remarks>The image belongs to the caller: dispose of it, or of this instance.</remarks>
public sealed class BitmapGlyphImage : IDisposable
{
    internal BitmapGlyphImage(Image<Rgba32> image, int pixelsPerEmX, int pixelsPerEmY, int left, int top, bool isMask)
    {
        Image = image;
        PixelsPerEmX = pixelsPerEmX;
        PixelsPerEmY = pixelsPerEmY;
        Left = left;
        Top = top;
        IsMask = isMask;
    }

    /// <summary>Gets the decoded image, at the size of its strike: one pixel of the image is one pixel of a text drawn at <see cref="PixelsPerEmX"/>.</summary>
    public Image<Rgba32> Image { get; }

    /// <summary>Gets the number of pixels of the image per em, horizontally: the size the strike is made for.</summary>
    public int PixelsPerEmX { get; }

    /// <summary>Gets the number of pixels of the image per em, vertically.</summary>
    public int PixelsPerEmY { get; }

    /// <summary>Gets the distance from the origin of the glyph to the left of the image, in pixels of the image.</summary>
    public int Left { get; }

    /// <summary>Gets the distance from the baseline up to the top of the image, in pixels of the image.</summary>
    public int Top { get; }

    /// <summary>
    /// Gets a value indicating whether the font gives the coverage of the pixels only (a bitmap without colors): the
    /// image is then black, with the coverage as its opacity, and a text draws it with its own color.
    /// </summary>
    public bool IsMask { get; }

    /// <summary>Disposes of the image.</summary>
    public void Dispose() => Image.Dispose();
}
