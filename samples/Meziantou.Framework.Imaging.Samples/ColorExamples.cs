using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/Imaging/09-Color), run by DocumentationExampleTests.</summary>
internal static class ColorExamples
{
    /// <summary>The EXIF orientation is reported by the decoder, and applied to the pixels on request.</summary>
    #region Imaging_ApplyExifOrientation
    public static ExifOrientation ApplyOrientation(string inputPath, string outputPath)
    {
        using var image = Image.Load(inputPath);

        // The orientation the camera recorded: the decoder reports it and leaves the pixels as they are stored
        var orientation = image.Metadata.Orientation;

        // Turns or mirrors every frame so that the pixels are upright, and sets the orientation to TopLeft: a viewer
        // that ignores the orientation then shows the same picture as one that applies it
        image.AutoOrient();

        // The orientation tag of the EXIF block is rewritten to match; the other metadata are kept
        image.Save(outputPath);
        return orientation;
    }
    #endregion

    /// <summary>A soft proof: the colors of a print, shown on a display, through a sequence of ICC profiles.</summary>
    #region Imaging_SoftProof
    public static ushort[] SoftProof(IccProfile display, IccProfile printer, ReadOnlySpan<ushort> rgb)
    {
        // The printer profile appears twice: the colors enter it from the profile connection space to become inks, then
        // leave it as the colors of these inks. The colors the printer cannot reproduce lose chroma at constant
        // lightness and hue, instead of being clipped channel by channel.
        var proof = IccColorTransform.Create([display, printer, printer, display], new IccColorTransformOptions
        {
            Intent = IccRenderingIntent.RelativeColorimetric,
            GamutMapping = GamutMapping.ReduceChroma,
        });

        // A transform is immutable and thread-safe: create it once and reuse it
        var preview = new ushort[rgb.Length];
        proof.Convert(rgb, preview);
        return preview;
    }
    #endregion
}
