namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Decodes the images of the glyphs a font gives as bitmaps (<c>sbix</c>, <c>CBDT</c>, <c>EBDT</c>, <c>bdat</c>): the
/// encoded images (PNG, JPEG, TIFF) with the codecs of the library, the pixels that are not encoded with the font code.
/// </summary>
internal static class BitmapGlyphDecoder
{
    /// <summary>Decodes the image of a bitmap glyph and makes the display list that draws it.</summary>
    /// <param name="face">The font.</param>
    /// <param name="bitmap">The image of the glyph in the font data.</param>
    /// <param name="size">The size the glyph is drawn at, in pixels per em.</param>
    /// <returns>The display list, or <see langword="null"/> for a bitmap without pixels, such as the one of a space, which draws nothing.</returns>
    /// <exception cref="InvalidImageContentException">The image is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature of its format that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of the configuration of the font.</exception>
    /// <exception cref="InvalidFontException">The pixels of the glyph are malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit.</exception>
    public static ColorGlyph? CreateGlyph(FontFace face, in BitmapGlyph bitmap, double size)
    {
        if (IsBlank(bitmap))
            return null;

        using var image = Decode(face, bitmap);

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

        var pixels = new byte[targetWidth * targetHeight * 4];
        image.Frames[0].CopyPixelBytesTo(pixels);
        var colorImage = new ColorImage(targetWidth, targetHeight, pixels);

        // A mask has the color of the text.
        return bitmap.Format == BitmapGlyphFormat.Mask
            ? ColorGlyph.FromMask(colorImage, left, top, width, height)
            : ColorGlyph.FromImage(colorImage, left, top, width, height);
    }

    /// <summary>Decodes the image of a bitmap glyph, at the size of its strike.</summary>
    /// <param name="face">The font.</param>
    /// <param name="bitmap">The image of the glyph in the font data.</param>
    /// <returns>The image: a mask is black, with the coverage of each pixel as its opacity.</returns>
    /// <exception cref="InvalidImageContentException">The image is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The image uses a feature of its format that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of the configuration of the font.</exception>
    /// <exception cref="InvalidFontException">The pixels of the glyph are malformed.</exception>
    /// <exception cref="FontResourceLimitException">The glyph exceeds the complexity limit.</exception>
    public static Image<Rgba32> Decode(FontFace face, in BitmapGlyph bitmap)
    {
        var configuration = ImageFontEnvironment.GetConfiguration(face);
        if (bitmap.Format is BitmapGlyphFormat.Mask or BitmapGlyphFormat.Bgra)
        {
            // The pixels the font code decodes: see IsBlank for an empty box.
            if (IsBlank(bitmap))
                throw new ArgumentException("The glyph has no pixel.", nameof(bitmap));

            var budget = new GlyphBudget(face.Data.Limits);
            var decoded = face.DecodeBitmap(bitmap, ref budget);
            var image = new Image<Rgba32>(decoded.Width, decoded.Height, configuration);
            try
            {
                image.Frames[0].ProcessPixelRows(decoded.Pixels, static (accessor, source) =>
                {
                    for (var y = 0; y < accessor.Height; y++)
                    {
                        var row = accessor.GetRowSpan(y);
                        var pixels = source.Slice(y * row.Length * 4, row.Length * 4);
                        for (var x = 0; x < row.Length; x++)
                        {
                            row[x] = new Rgba32(pixels[x * 4], pixels[(x * 4) + 1], pixels[(x * 4) + 2], pixels[(x * 4) + 3]);
                        }
                    }
                });
            }
            catch
            {
                image.Dispose();
                throw;
            }

            return image;
        }

        var data = face.Data.Bytes.Span.Slice(bitmap.Offset, bitmap.Length);
        try
        {
            // The data is an image of the format the font gives for it, not of any format the library reads.
            if (Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration }).Format != ToImageFormat(bitmap.Format))
                throw new InvalidImageContentException("The font is invalid: the image of a glyph is not in the format the font gives for it.", ImageFormat.Unknown);

            return Image.Load<Rgba32>(data, new ImageDecodeOptions { Configuration = configuration, FrameLimit = 1 });
        }
        catch (UnknownImageFormatException exception)
        {
            throw new InvalidImageContentException("The font is invalid: the image of a glyph is not an image the font format allows.", ImageFormat.Unknown, exception);
        }
    }

    /// <summary>
    /// Tells whether a bitmap glyph has no pixels: a bitmap that is not encoded with a width or a height of 0, such as
    /// the one of a space. It draws nothing.
    /// </summary>
    public static bool IsBlank(in BitmapGlyph bitmap) => bitmap.Format is BitmapGlyphFormat.Mask or BitmapGlyphFormat.Bgra && (bitmap.Width == 0 || bitmap.Height == 0);

    private static ImageFormat ToImageFormat(BitmapGlyphFormat format) => format switch
    {
        BitmapGlyphFormat.Png => ImageFormat.Png,
        BitmapGlyphFormat.Jpeg => ImageFormat.Jpeg,
        BitmapGlyphFormat.Tiff => ImageFormat.Tiff,
        _ => ImageFormat.Unknown,
    };
}
