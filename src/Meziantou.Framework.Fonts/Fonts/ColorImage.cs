namespace Meziantou.Framework.Fonts;

/// <summary>The image of a glyph a font gives as a bitmap: pixels decoded, and scaled to the size the glyph is drawn at, when a text is laid out.</summary>
internal sealed class ColorImage
{
    public ColorImage(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the pixels, row by row from the top, four bytes each (red, green, blue, opacity), with colors that are not multiplied by their opacity.</summary>
    public byte[] Pixels { get; }
}
