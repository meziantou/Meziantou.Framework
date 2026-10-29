using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Samples;

/// <summary>Examples of the documentation site (docs/01-Imaging/09-Color), run by DocumentationExampleTests.</summary>
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
}
