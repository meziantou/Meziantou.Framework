namespace Meziantou.Framework.Imaging;

/// <summary>Identifies one of the built-in working pixel representations used to store decoded pixels.</summary>
/// <remarks>
/// A pixel format describes the in-memory storage of an <see cref="Image{TPixel}"/>, not the compressed
/// encoding of a file. Use <see cref="ImageInfo.ColorModel"/> and <see cref="ImageInfo.BitsPerComponent"/>
/// for information about the source encoding.
/// </remarks>
public enum PixelFormat
{
    /// <summary>Unknown or unspecified. Never returned by an <see cref="Image"/>.</summary>
    Unknown = 0,

    /// <summary>8-bit red, green, blue and straight alpha components, stored in that byte order (<see cref="Imaging.Rgba32"/>).</summary>
    Rgba32 = 1,

    /// <summary>8-bit blue, green, red and straight alpha components, stored in that byte order (<see cref="Imaging.Bgra32"/>).</summary>
    Bgra32 = 2,

    /// <summary>8-bit red, green and blue components without alpha (<see cref="Imaging.Rgb24"/>).</summary>
    Rgb24 = 3,

    /// <summary>16-bit red, green, blue and straight alpha components in native endianness (<see cref="Imaging.Rgba64"/>).</summary>
    Rgba64 = 4,

    /// <summary>8-bit grayscale without alpha (<see cref="Imaging.Gray8"/>).</summary>
    Gray8 = 5,

    /// <summary>16-bit grayscale without alpha in native endianness (<see cref="Imaging.Gray16"/>).</summary>
    Gray16 = 6,
}
