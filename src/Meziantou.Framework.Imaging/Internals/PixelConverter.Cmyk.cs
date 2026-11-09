using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The conversions between the CMYK formats, which only change the precision of the inks.</summary>
/// <remarks>
/// Converting CMYK pixels to or from another format converts colors: it is not a layout conversion, and only a
/// <see cref="PixelConversionPlan"/> with <see cref="PixelConversionOptions.CmykConversion"/> does it
/// (<see cref="CmykColorConversion"/>). The row converters reject it.
/// </remarks>
internal static partial class PixelConverter
{
    /// <summary>Gets a value indicating whether a pixel type stores CMYK inks.</summary>
    internal static bool IsCmyk<TPixel>() => typeof(TPixel) == typeof(Cmyk32) || typeof(TPixel) == typeof(Cmyk64);

    /// <summary>Creates the error of a conversion between CMYK pixels and pixels of another kind without color conversion.</summary>
    /// <param name="sourceFormat">The source pixel format.</param>
    /// <param name="destinationFormat">The destination pixel format.</param>
    /// <param name="format">The image format reported in the exception.</param>
    /// <returns>The exception.</returns>
    internal static UnsupportedImageFeatureException CreateCmykConversionException(PixelFormat sourceFormat, PixelFormat destinationFormat, ImageFormat format = ImageFormat.Unknown)
        => new($"Converting {sourceFormat} pixels to {destinationFormat} converts colors between CMYK inks and light. Set PixelConversionOptions.CmykConversion to convert them, with the ICC profile of the image or the device CMYK formulas.", format, "CMYK color conversion");

    // 8 to 16 bits is exact (v * 257), 16 to 8 bits rounds to nearest
    private static void ConvertCmykPrecision(PixelFormat sourceFormat, ReadOnlySpan<byte> source, PixelFormat destinationFormat, Span<byte> destination, ImageFormat format)
    {
        switch (sourceFormat, destinationFormat)
        {
            case (PixelFormat.Cmyk32, PixelFormat.Cmyk64):
                {
                    var target = unsafe(MemoryMarshal.Cast<byte, ushort>(destination));
                    for (var i = 0; i < source.Length; i++)
                    {
                        target[i] = To16Bit(source[i]);
                    }

                    break;
                }

            case (PixelFormat.Cmyk64, PixelFormat.Cmyk32):
                {
                    var samples = unsafe(MemoryMarshal.Cast<byte, ushort>(source));
                    for (var i = 0; i < samples.Length; i++)
                    {
                        destination[i] = To8Bit(samples[i]);
                    }

                    break;
                }

            case (PixelFormat.Cmyk32, PixelFormat.Cmyk32):
            case (PixelFormat.Cmyk64, PixelFormat.Cmyk64):
                source.CopyTo(destination);
                break;

            default:
                throw CreateCmykConversionException(sourceFormat, destinationFormat, format);
        }
    }
}
