namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The image of a glyph a font gives as a bitmap: pixels decoded, and scaled to the size the glyph is drawn at, when a text is laid out.</summary>
internal sealed class ColorImage
{
    public ColorImage(int width, int height, Rgba32[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the pixels, row by row from the top, with colors that are not multiplied by their opacity.</summary>
    public Rgba32[] Pixels { get; }

    /// <summary>Decodes the image of a bitmap glyph and makes the display list that draws it.</summary>
    /// <param name="face">The font.</param>
    /// <param name="bitmap">The image of the glyph in the font data.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <exception cref="InvalidImageContentException">The image is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature of its format that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of the configuration of the font.</exception>
    public static ColorGlyph CreateGlyph(FontFace face, in BitmapGlyph bitmap, double size)
    {
        var data = face.Data.Bytes.AsSpan(bitmap.Offset, bitmap.Length);
        Image<Rgba32> image;
        try
        {
            image = Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = face.Data.Configuration, FrameLimit = 1 });
        }
        catch (UnknownImageFormatException exception)
        {
            throw new InvalidImageContentException("The font is invalid: the image of a glyph is not an image the font format allows.", ImageFormat.Unknown, exception);
        }

        using (image)
        {
            // The image as the font places it, in font units: a pixel of the strike is a fraction of the em.
            var sourceWidth = image.Width;
            var sourceHeight = image.Height;
            var unitsPerPixelX = (double)face.UnitsPerEm / bitmap.PixelsPerEmX;
            var unitsPerPixelY = (double)face.UnitsPerEm / bitmap.PixelsPerEmY;
            var left = bitmap.Left * unitsPerPixelX;
            var top = bitmap.Bottom is { } bottom ? (bottom + sourceHeight) * unitsPerPixelY : bitmap.Top * unitsPerPixelY;
            var width = sourceWidth * unitsPerPixelX;
            var height = sourceHeight * unitsPerPixelY;

            // An image larger than the glyph is drawn is scaled down once, with the filter of the library: sampling it
            // at a pixel in several would skip most of it. A smaller one is kept, and interpolated when it is drawn.
            var targetWidth = Math.Clamp((int)Math.Round(sourceWidth * size / bitmap.PixelsPerEmX, MidpointRounding.AwayFromZero), 1, sourceWidth);
            var targetHeight = Math.Clamp((int)Math.Round(sourceHeight * size / bitmap.PixelsPerEmY, MidpointRounding.AwayFromZero), 1, sourceHeight);
            if (targetWidth != sourceWidth || targetHeight != sourceHeight)
            {
                image.Resize(new ResizeOptions(targetWidth, targetHeight) { Mode = ResizeMode.Stretch });
            }

            var pixels = new Rgba32[targetWidth * targetHeight];
            image.Frames[0].CopyPixelDataTo(pixels);
            FontErrors.EnsureCoordinates(left, top);
            FontErrors.EnsureCoordinates(left + width, top - height);
            return new ColorGlyph
            {
                Ops = [new ColorOp(ColorOpKind.FillImage, 0, -1, 0)],
                Paints = [new ColorPaint(ColorPaintKind.Image, -1, 0, 0, 0, 0)],
                Stops = [],

                // The left and the top of the image, and the size of one of its pixels.
                Numbers = [left, top, width / targetWidth, height / targetHeight],
                XMin = left,
                YMin = top - height,
                XMax = left + width,
                YMax = top,
                LayerDepth = 1,
                ClipDepth = 0,
                Image = new ColorImage(targetWidth, targetHeight, pixels),
            };
        }
    }
}
