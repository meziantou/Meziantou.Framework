namespace Meziantou.Framework.Imaging;

/// <summary>Selects the pixels an OpenEXR file is decoded to (<see cref="OpenExrDecodingOptions.WindowMode"/>).</summary>
/// <remarks>
/// An OpenEXR file has two windows, in the same pixel coordinates: the display window, the area the image is meant to
/// show, and the data window, the pixels the file stores. Renderers often store an overscan, a data window larger than the
/// display window, and crops store a data window smaller than it. <see cref="Metadata.ImageMetadata.OpenExrWindows"/>
/// gives the two windows of a decoded file.
/// </remarks>
public enum OpenExrWindowMode
{
    /// <summary>
    /// The image is the display window: the pixels of the data window are placed in it, and the pixels it does not cover are
    /// transparent black. A file whose data window has pixels outside the display window is rejected with an
    /// <see cref="UnsupportedImageFeatureException"/>, since they would be lost. This is the default.
    /// </summary>
    DisplayWindow = 0,

    /// <summary>
    /// The image is the display window, as with <see cref="DisplayWindow"/>, but the pixels of the data window outside it are
    /// discarded: the image is cropped to the display window.
    /// </summary>
    CropToDisplayWindow = 1,

    /// <summary>The image is the data window: every pixel the file stores, and nothing else.</summary>
    DataWindow = 2,
}
