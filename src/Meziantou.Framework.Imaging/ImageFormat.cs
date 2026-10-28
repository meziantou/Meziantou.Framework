namespace Meziantou.Framework.Imaging;

/// <summary>Identifies an encoded image file format.</summary>
/// <remarks>
/// Formats are detected from content, never from file names. Animated PNG (APNG) is reported as <see cref="Png"/>:
/// it is a PNG variant, not a competing signature. New formats are added as new members.
/// </remarks>
public enum ImageFormat
{
    /// <summary>The format is unknown or not recognized.</summary>
    Unknown = 0,

    /// <summary>Portable Network Graphics, including animated PNG (APNG).</summary>
    /// <remarks>
    /// Apple's CgBI variant (the iOS-optimized PNG files of application bundles: premultiplied BGRA samples in a raw
    /// deflate datastream) is decoded too, and never written. Its colors are un-premultiplied to <c>round(c * 255 / a)</c>,
    /// as Apple ImageIO decodes them: this is lossy for low alpha values (a color with an alpha of 1 can only be 0 or 255).
    /// </remarks>
    Png = 1,

    /// <summary>Graphics Interchange Format (GIF87a and GIF89a).</summary>
    Gif = 2,

    /// <summary>JPEG (JFIF/EXIF) sequential and progressive images, Huffman coded or arithmetic coded.</summary>
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

    /// <summary>
    /// AV1 Image File Format: still images and image sequences coded with AV1 in an ISO base media (HEIF) container.
    /// </summary>
    Avif = 13,

    /// <summary>
    /// JPEG XL (ISO/IEC 18181): a bare codestream or its container, still images and animations, lossy (VarDCT) and
    /// lossless (Modular) coding. Encoding is lossless.
    /// </summary>
    JpegXl = 14,

    /// <summary>
    /// High Efficiency Image File Format images coded with HEVC (H.265): still images and image sequences in an ISO base
    /// media (HEIF) container.
    /// </summary>
    Heic = 15,

    /// <summary>
    /// JPEG 2000 (ISO/IEC 15444-1): a codestream or a JP2 file, coded with the reversible or the irreversible wavelet
    /// transformation. Codestreams that use the high-throughput block coder (ISO/IEC 15444-15, HTJ2K) are read.
    /// </summary>
    Jpeg2000 = 16,

    /// <summary>
    /// OpenEXR: high dynamic range scene-linear images of 16-bit (half) or 32-bit floating-point samples, stored as scan
    /// lines or tiles with lossless or lossy compression.
    /// </summary>
    OpenExr = 17,
}
