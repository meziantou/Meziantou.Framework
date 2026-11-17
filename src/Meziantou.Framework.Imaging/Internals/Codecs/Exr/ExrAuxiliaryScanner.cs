namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Lists the images of an OpenEXR file that the image does not present (<see cref="AuxiliaryImageCollection"/>): the
/// preview image of its <c>preview</c> attribute, and the levels of a tiled image with mipmap or ripmap levels other than
/// the full resolution.
/// </summary>
internal static class ExrAuxiliaryScanner
{
    /// <summary>Lists the images of the part and the channels the configuration selects.</summary>
    /// <returns>The images, or none when the image itself cannot be decoded with the configuration.</returns>
    /// <exception cref="InvalidImageContentException">The header is malformed.</exception>
    public static List<AuxiliaryImage> Scan(AuxiliaryImageCollection collection, byte[] data, ImageConfiguration configuration, CancellationToken cancellationToken)
    {
        var context = new ImageCodecContext(configuration, "AuxiliaryImages", cancellationToken);
        ExrHeader header;
        using (var parser = new ExrStructureParser(context, StructureWalk.Header, level: (0, 0)))
        {
            try
            {
                _ = ImageInputPump.Run(data, parser, context);
            }
            catch (UnsupportedImageFeatureException)
            {
                // The images of a file whose image cannot be decoded cannot be either
                return [];
            }

            header = parser.Header;
        }

        var images = new List<AuxiliaryImage>();
        if (header.Preview is var (width, height, pixels))
        {
            images.Add(new AuxiliaryImage(collection, AuxiliaryImageKind.Thumbnail, auxiliaryType: null, new Size(width, height)) { OpenExrPreview = pixels });
        }

        if (header.IsTiled && header.LevelMode != ExrLevelMode.OneLevel)
        {
            for (var y = 0; y < header.YLevelCount; y++)
            {
                for (var x = 0; x < header.XLevelCount; x++)
                {
                    // The levels of a mipmap are on the diagonal; the full resolution is the image
                    if ((x == 0 && y == 0) || (header.LevelMode == ExrLevelMode.MipmapLevels && x != y))
                        continue;

                    var size = new Size(header.GetLevelSize(x, horizontal: true), header.GetLevelSize(y, horizontal: false));
                    images.Add(new AuxiliaryImage(collection, AuxiliaryImageKind.ResolutionLevel, auxiliaryType: null, size) { Level = new Point(x, y) });
                }
            }
        }

        return images;
    }

    /// <summary>
    /// Decodes the preview image: 8-bit RGBA pixels, whose colors are the gamma-corrected colors of the image, as a display
    /// shows them (sRGB), and whose alpha is not multiplied into them.
    /// </summary>
    public static Image DecodePreview(AuxiliaryImage image, byte[] pixels, ImageDecodeOptions options, PixelFormat? pixelFormat)
    {
        var preview = Image.ImportPixelBytes<Rgba32>(pixels, image.Size.Width, image.Size.Height, configuration: options.Configuration);
        if (pixelFormat is not { } format || format == PixelFormat.Rgba32)
            return preview;

        using (preview)
        {
            return preview.CloneAs(format, options.Conversion);
        }
    }
}
