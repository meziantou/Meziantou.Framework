using Meziantou.Framework.Imaging.Formats;
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

    /// <summary>The thumbnails, depth maps and gain maps a photo stores next to its main image, decoded on request.</summary>
    #region Imaging_HdrGainMap
    public static IReadOnlyList<AuxiliaryImageKind> SaveHdrRendition(string photoPath, string exrPath)
    {
        // Image.Load reads the main image only. The other images of the file are listed here, from its structure, and
        // each one is decoded on request: the gain map of an Ultra HDR JPEG, the thumbnails, depth maps, mattes and
        // gain maps of a HEIC or AVIF photo
        var images = AuxiliaryImageCollection.Load(photoPath);
        var kinds = images.Select(image => image.Kind).ToList();

        // The gain map turns the standard dynamic range photo into its high dynamic range version. The headroom of the
        // display, in stops (log2 of its peak light over the white of SDR), selects how much of the gain applies: the
        // headroom of the alternate rendition gives all of it. The result is linear light, 1 being the white of SDR
        if (images.GainMapImage?.GainMapMetadata is { } metadata)
        {
            using var hdr = images.DecodeHdr(metadata.AlternateHdrHeadroom);

            // OpenEXR stores the floating-point samples, and the primaries as its chromaticities
            hdr.Save(exrPath, new OpenExrEncoder());
        }

        return kinds;
    }
    #endregion
}
