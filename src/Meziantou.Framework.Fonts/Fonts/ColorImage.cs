namespace Meziantou.Framework.Fonts;

/// <summary>The image of a glyph a font gives as a bitmap: pixels decoded, and scaled to the size the glyph is drawn at, when a text is laid out.</summary>
public sealed class ColorImage
{
    private readonly byte[] _pixels;

    /// <summary>Initializes a new instance of the <see cref="ColorImage"/> class.</summary>
    /// <param name="width">The width of the image, in pixels.</param>
    /// <param name="height">The height of the image, in pixels.</param>
    /// <param name="pixels">The pixels, row by row from the top, four bytes each (red, green, blue, opacity), with colors that are not multiplied by their opacity.</param>
    public ColorImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        ArgumentNullException.ThrowIfNull(pixels);
        _pixels = pixels;
    }

    /// <summary>Gets the width of the image, in pixels.</summary>
    public int Width { get; }

    /// <summary>Gets the height of the image, in pixels.</summary>
    public int Height { get; }

    /// <summary>Gets the pixels, row by row from the top, four bytes each (red, green, blue, opacity), with colors that are not multiplied by their opacity.</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;
}
