namespace Meziantou.Framework.Imaging;

/// <summary>Identifies an encoded image file format.</summary>
/// <remarks>
/// Formats are detected from content, never from file names. Animated PNG (APNG) is reported as <see cref="Png"/>:
/// it is a PNG variant, not a competing signature. Follow-up formats (JPEG XL, AVIF) will be added as new
/// members when they are implemented.
/// </remarks>
public enum ImageFormat
{
    /// <summary>The format is unknown or not recognized.</summary>
    Unknown = 0,

    /// <summary>Portable Network Graphics, including animated PNG (APNG).</summary>
    Png = 1,

    /// <summary>Graphics Interchange Format (GIF87a and GIF89a).</summary>
    Gif = 2,

    /// <summary>JPEG (JFIF/EXIF) baseline and progressive Huffman-coded images.</summary>
    Jpeg = 3,

    /// <summary>WebP (RIFF container): lossy VP8 and lossless VP8L still images and animations.</summary>
    WebP = 4,

    /// <summary>Quite OK Image format: lossless 8-bit RGB and RGBA still images.</summary>
    Qoi = 5,

    /// <summary>Windows BMP (device-independent bitmap), uncompressed subset.</summary>
    Bmp = 6,

    /// <summary>Truevision TGA (TARGA), true-color, grayscale and color-mapped still images.</summary>
    Tga = 7,

    /// <summary>Netpbm portable anymap family: PBM, PGM, PPM (<c>P1</c> to <c>P6</c>) and PAM (<c>P7</c>).</summary>
    Pnm = 8,

    /// <summary>
    /// Tagged Image File Format, including BigTIFF. A TIFF file is a document of one or more pages; use
    /// <see cref="ImageCollection"/> to reach pages other than the first one.
    /// </summary>
    Tiff = 9,

    /// <summary>
    /// Windows icon container. An icon file holds alternative representations of one drawing; use
    /// <see cref="ImageCollection"/> to reach representations other than the default one.
    /// </summary>
    Ico = 10,

    /// <summary>Windows cursor container: an icon directory whose entries also carry a hotspot.</summary>
    Cur = 11,

    /// <summary>
    /// Windows animated cursor (RIFF <c>ACON</c> container): an animation whose frames are icon or cursor images, each
    /// with its own hotspot (<see cref="Metadata.FrameMetadata.Hotspot"/>).
    /// </summary>
    Ani = 12,
}
