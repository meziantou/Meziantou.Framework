using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The pixel formats that most codecs and some kernels do not handle themselves (<see cref="PixelFormat.La16"/>,
/// <see cref="PixelFormat.La32"/>, <see cref="PixelFormat.Rgb48"/> and <see cref="PixelFormat.A8"/>), and the format each
/// one widens to without any loss for them.
/// </summary>
/// <remarks>
/// <see cref="PixelFormat.La16"/> and <see cref="PixelFormat.A8"/> widen to <see cref="PixelFormat.Rgba32"/>, and
/// <see cref="PixelFormat.La32"/> and <see cref="PixelFormat.Rgb48"/> to <see cref="PixelFormat.Rgba64"/> (gray replicated
/// in the three colors, <see cref="PixelFormat.A8"/> black, alpha added fully opaque): the conversion back is exact, as the
/// luma of a gray color is that gray. An encoder that stores <see cref="PixelFormat.La16"/> gets
/// <see cref="PixelFormat.A8"/> as <see cref="PixelFormat.La16"/>.
/// </remarks>
internal static class PixelFormatWidening
{
    /// <summary>Gets a value indicating whether a format is one of the formats that widen.</summary>
    public static bool IsWidened(PixelFormat format) => format is PixelFormat.La16 or PixelFormat.La32 or PixelFormat.Rgb48 or PixelFormat.A8;

    /// <summary>Gets the format a format widens to, or the format itself.</summary>
    public static PixelFormat GetWidenedFormat(PixelFormat format) => format switch
    {
        PixelFormat.La16 or PixelFormat.A8 => PixelFormat.Rgba32,
        PixelFormat.La32 or PixelFormat.Rgb48 => PixelFormat.Rgba64,
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
