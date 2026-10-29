using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel formats that most codecs and some kernels do not handle themselves (<see cref="PixelFormat.La16"/>,
/// <see cref="PixelFormat.La32"/>, <see cref="PixelFormat.Rgb48"/>, <see cref="PixelFormat.A8"/> and the interoperability
/// formats <see cref="PixelFormat.Argb32"/>, <see cref="PixelFormat.Abgr32"/>, <see cref="PixelFormat.Bgr565"/> and
/// <see cref="PixelFormat.Rgba1010102"/>), and the format each one widens to without any loss for them.
/// </summary>
/// <remarks>
/// <see cref="PixelFormat.La16"/>, <see cref="PixelFormat.A8"/>, <see cref="PixelFormat.Argb32"/> and
/// <see cref="PixelFormat.Abgr32"/> widen to <see cref="PixelFormat.Rgba32"/>, <see cref="PixelFormat.Bgr565"/> to
/// <see cref="PixelFormat.Rgb24"/>, and <see cref="PixelFormat.La32"/>, <see cref="PixelFormat.Rgb48"/> and
/// <see cref="PixelFormat.Rgba1010102"/> to <see cref="PixelFormat.Rgba64"/> (gray replicated in the three colors,
/// <see cref="PixelFormat.A8"/> black, alpha added fully opaque, packed components to the nearest sample): the conversion
/// back is exact, as the luma of a gray color is that gray and a packed component goes back to itself through these
/// precisions. An encoder that stores <see cref="PixelFormat.La16"/> gets <see cref="PixelFormat.A8"/> as
/// <see cref="PixelFormat.La16"/>.
/// </remarks>
internal static class PixelFormatWidening
{
    /// <summary>Gets a value indicating whether a format is one of the formats that widen.</summary>
    public static bool IsWidened(PixelFormat format) => format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 or PixelFormat.Bgr565 or PixelFormat.Rgba1010102;

    /// <summary>Gets the format a format widens to, or the format itself.</summary>
    public static PixelFormat GetWidenedFormat(PixelFormat format) => format switch
    {
        PixelFormat.La16 or PixelFormat.A8 or PixelFormat.Argb32 or PixelFormat.Abgr32 => PixelFormat.Rgba32,
        PixelFormat.Bgr565 => PixelFormat.Rgb24,
        PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.Rgba1010102 => PixelFormat.Rgba64,
        _ => format,
    };

    /// <summary>Gets the format an encoder is given for frames of a format: the format itself when the encoder stores it, else its widening.</summary>
    public static PixelFormat GetEncodedFormat(ImageEncoder encoder, PixelFormat format)
    {
        if (!IsWidened(format) || encoder.StoresPixelFormat(format))
            return format;

        if (format == PixelFormat.A8 && encoder.StoresPixelFormat(PixelFormat.La16))
            return PixelFormat.La16;

        return GetWidenedFormat(format);
    }
}
