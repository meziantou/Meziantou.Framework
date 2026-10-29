using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/01-Imaging/10-Formats), run by DocumentationExampleTests.</summary>
internal static class FormatExamples
{
    /// <summary>GIF output: how alpha and colors are reduced.</summary>
    #region Imaging_SaveGif
    public static void SaveGif(string inputPath, string gifPath, string flattenedGifPath)
    {
        using var image = Image.Load(inputPath);

        // GIF has one fully transparent palette entry and no partial alpha: pixels whose alpha is below the threshold
        // become transparent, the others opaque. 64 colors per frame (the transparent entry included), dithered.
        image.Save(gifPath, new GifEncoder
        {
            AlphaThreshold = 64,
            Quantization = new QuantizeOptions { MaxColors = 64, Dithering = DitheringMode.FloydSteinberg },
            MetadataHandling = MetadataHandling.DiscardUnsupported, // GIF only stores comments: drop the ICC profile, EXIF...
        });

        // Soft edges look better composited over the color of the page that shows the image
        image.Save(flattenedGifPath, new GifEncoder
        {
            AlphaMode = GifAlphaMode.Flatten,
            BackgroundColor = new Rgba32(255, 255, 255),
            Interlaced = true,
            MetadataHandling = MetadataHandling.DiscardUnsupported,
        });
    }
    #endregion

    /// <summary>TGA output, and the detection of a format without a signature.</summary>
    #region Imaging_SaveTga
    public static ImageFormat SaveTga(string inputPath, string tgaPath)
    {
        using var image = Image.Load(inputPath);

        // TGA stores one still image: save the first frame. Run-length packets are lossless and usually smaller on flat
        // colors; uncompressed rows (the default) are the most widely readable. 16-bit images are rounded to 8 bits only
        // with consent.
        image.Frames[0].Save(tgaPath, new TgaEncoder { Compression = TgaCompression.RunLength, AllowBitDepthReduction = true });

        // TGA has no signature: DetectFormat recognizes it from its whole 18-byte header
        var header = new byte[Image.FormatDetectionPrefixLength];
        using var stream = File.OpenRead(tgaPath);
        stream.ReadExactly(header);
        return Image.DetectFormat(header);
    }
    #endregion

    /// <summary>Plain (text) Netpbm output, for test fixtures.</summary>
    #region Imaging_SavePlainNetpbm
    public static void SavePlainNetpbm(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // Netpbm stores one still image: save the first frame. Decimal samples that a text editor and a diff can show. The
        // variant still follows the pixel format: P2 (PGM) for gray pixels, P3 (PPM) otherwise. The plain forms have no
        // alpha, so pixels that are not opaque are composited over the background color.
        image.Frames[0].Save(outputPath, new PnmEncoder { Encoding = PnmEncoding.Plain, BackgroundColor = new Rgba32(255, 255, 255) });
    }
    #endregion

    /// <summary>Animated cursor output with explicit settings.</summary>
    #region Imaging_SaveAni
    public static void SaveAnimatedCursor(string animationPath, string aniPath)
    {
        using var animation = Image.Load(animationPath);

        // An animated cursor always loops: a finite play count is rejected, never dropped
        animation.Animation = new AnimationMetadata();
        animation.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Busy"));
        animation.Metadata.TextEntries.Add(new ImageTextEntry("Author", "Contoso"));

        animation.Save(aniPath, new AniEncoder
        {
            // Auto writes 32-bit bitmaps up to 64 pixels per side, and PNG above
            PayloadFormat = IconPayloadFormat.Png,

            // A step lasts a whole number of sixtieths of a second: fail instead of rounding the other durations
            DurationRounding = FrameDurationRounding.RequireExact,
        });
    }
    #endregion
}
