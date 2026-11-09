using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Metadata;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The color conversion of a <see cref="PixelConversionPlan"/> between CMYK pixels and the pixels of another format
/// (<see cref="PixelConversionOptions.CmykConversion"/>), through RGB rows of 8 or 16 bits.
/// </summary>
/// <remarks>
/// <para>
/// From CMYK: the inks are converted to an RGB row (<see cref="Rgb24"/> or <see cref="Rgb48"/>) by the ICC pipeline, or by
/// the device formulas of <see cref="Cmyk"/>, then the RGB row is converted to the destination by the plan of the layout
/// conversion (<see cref="_layout"/>). To CMYK: the plan of the layout converts the source to an RGB row (alpha removal,
/// floating-point samples, gray replicated), which is converted to the inks.
/// </para>
/// <para>
/// The device formulas are evaluated in <see cref="double"/> on exact fractions and rounded once: the RGB row has the
/// precision of the destination (from CMYK) or of the source (to CMYK). The ICC pipeline converts samples of one type: its
/// rows have 16 bits when either side has more than 8 bits (8-bit inks are widened exactly first), and the 16-bit inks are
/// rounded to the 8 bits of <see cref="Cmyk32"/> after it.
/// </para>
/// <para>The rows are converted in chunks of <see cref="IccPipeline.BatchSize"/> pixels on the stack: nothing is allocated per row.</para>
/// </remarks>
internal sealed class CmykColorConversion
{
    private const int ChunkPixels = IccPipeline.BatchSize;

    private readonly IccPipeline? _pipeline;
    private readonly PixelConversionPlan _layout;
    private readonly bool _fromCmyk;
    private readonly PixelFormat _cmykFormat;
    private readonly PixelFormat _rgbFormat;

    private CmykColorConversion(PixelFormat cmykFormat, bool fromCmyk, IccPipeline? pipeline, PixelFormat rgbFormat, PixelConversionPlan layout, IccProfile? profile)
    {
        _cmykFormat = cmykFormat;
        _fromCmyk = fromCmyk;
        _pipeline = pipeline;
        _rgbFormat = rgbFormat;
        _layout = layout;
        ColorProfile = profile;
    }

    /// <summary>Gets the profile of the converted pixels, or <see langword="null"/>.</summary>
    public IccProfile? ColorProfile { get; }

    /// <summary>Gets the plan of the layout conversion: RGB row to destination (from CMYK), source to RGB row (to CMYK).</summary>
    public PixelConversionPlan Layout => _layout;

    /// <summary>Gets a value indicating whether the conversion is from CMYK pixels.</summary>
    public bool FromCmyk => _fromCmyk;

    /// <summary>Creates the conversion between a CMYK format and another format.</summary>
    /// <param name="sourceFormat">The source format.</param>
    /// <param name="destinationFormat">The destination format; exactly one of the two is a CMYK format.</param>
    /// <param name="options">The options of the plan, whose <see cref="PixelConversionOptions.CmykConversion"/> is set.</param>
    /// <param name="sourceProfile">The profile of the source pixels, or <see langword="null"/>.</param>
    /// <param name="format">The image format reported in exceptions.</param>
    /// <returns>The conversion.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The source profile cannot label the source pixels, or a profile cannot be used.</exception>
    /// <exception cref="InvalidImageContentException">A profile is malformed.</exception>
    public static CmykColorConversion Create(PixelFormat sourceFormat, PixelFormat destinationFormat, PixelConversionOptions options, IccProfile? sourceProfile, ImageFormat format)
    {
        var cmyk = options.CmykConversion!;
        if (PixelFormats.IsCmyk(sourceFormat))
        {
            // A profile of another color space does not describe the inks: it is not used
            var cmykProfile = ColorProfileCompatibility.Resolve(sourceProfile, sourceFormat, options.DiscardIncompatibleColorProfile, format) ?? cmyk.CmykProfile;
            var pipeline = cmykProfile is null ? null : IccColorTransform.Create(cmykProfile, cmyk.RgbProfile, cmyk.Transform).Pipeline;
            var deep = pipeline is null ? Is16Bit(destinationFormat) : Is16Bit(sourceFormat) || Is16Bit(destinationFormat);
            var rgbFormat = deep ? PixelFormat.Rgb48 : PixelFormat.Rgb24;
            var layout = PixelConversionPlan.Create(rgbFormat, destinationFormat, options, pipeline is null ? null : cmyk.RgbProfile, format);
            return new CmykColorConversion(sourceFormat, fromCmyk: true, pipeline, rgbFormat, layout, layout.ColorProfile);
        }
        else
        {
            var deep = cmyk.CmykProfile is null ? Is16Bit(sourceFormat) : Is16Bit(sourceFormat) || Is16Bit(destinationFormat);
            var rgbFormat = deep ? PixelFormat.Rgb48 : PixelFormat.Rgb24;

            // The layout conversion checks the profile of the source against RGB pixels (a gray profile is incompatible)
            var layout = PixelConversionPlan.Create(sourceFormat, rgbFormat, options, sourceProfile, format);
            var pipeline = cmyk.CmykProfile is null ? null : IccColorTransform.Create(layout.ColorProfile ?? cmyk.RgbProfile, cmyk.CmykProfile, cmyk.Transform).Pipeline;
            return new CmykColorConversion(destinationFormat, fromCmyk: false, pipeline, rgbFormat, layout, cmyk.CmykProfile);
        }
    }

    /// <summary>Converts a row of raw native-endian pixels.</summary>
    /// <param name="source">The source bytes.</param>
    /// <param name="destination">The destination bytes, for as many pixels.</param>
    /// <param name="sourceFormat">The source format.</param>
    /// <param name="destinationFormat">The destination format.</param>
    public void ConvertRow(ReadOnlySpan<byte> source, Span<byte> destination, PixelFormat sourceFormat, PixelFormat destinationFormat)
    {
        var sourceBytes = PixelFormats.GetBytesPerPixel(sourceFormat);
        var destinationBytes = PixelFormats.GetBytesPerPixel(destinationFormat);
        var rgbBytes = PixelFormats.GetBytesPerPixel(_rgbFormat);
        Span<byte> rgb = stackalloc byte[ChunkPixels * 6];
        Span<ushort> inks = stackalloc ushort[ChunkPixels * 4];
        var width = source.Length / sourceBytes;
        for (var start = 0; start < width; start += ChunkPixels)
        {
            var count = Math.Min(ChunkPixels, width - start);
            var sourceChunk = source.Slice(start * sourceBytes, count * sourceBytes);
            var destinationChunk = destination.Slice(start * destinationBytes, count * destinationBytes);
            var rgbChunk = rgb[..(count * rgbBytes)];
            if (_fromCmyk)
            {
                ToRgb(sourceChunk, rgbChunk, inks[..(count * 4)]);
                _layout.ConvertRow(rgbChunk, destinationChunk);
            }
            else
            {
                _layout.ConvertRow(sourceChunk, rgbChunk);
                ToCmyk(rgbChunk, destinationChunk, inks[..(count * 4)]);
            }
        }
    }

    private static bool Is16Bit(PixelFormat format) => PixelFormats.GetBitsPerComponent(format) > 8;

    private static double ReadFraction(ReadOnlySpan<byte> samples, int index, bool deep)
        => deep ? unsafe(MemoryMarshal.Cast<byte, ushort>(samples))[index] / 65535.0 : samples[index] / 255.0;

    private static void WriteFraction(Span<byte> samples, int index, bool deep, double value)
    {
        if (deep)
        {
            unsafe { MemoryMarshal.Cast<byte, ushort>(samples)[index] = ColorValueMath.ToUInt16(value); }
        }
        else
        {
            samples[index] = ColorValueMath.ToByte(value);
        }
    }

    private void ToRgb(ReadOnlySpan<byte> source, Span<byte> rgb, Span<ushort> inks)
    {
        var count = inks.Length / 4;
        var deepInks = _cmykFormat == PixelFormat.Cmyk64;
        var deepRgb = _rgbFormat == PixelFormat.Rgb48;
        if (_pipeline is null)
        {
            for (var i = 0; i < count; i++)
            {
                var color = new Cmyk(ReadFraction(source, 4 * i, deepInks), ReadFraction(source, (4 * i) + 1, deepInks), ReadFraction(source, (4 * i) + 2, deepInks), ReadFraction(source, (4 * i) + 3, deepInks));
                var (red, green, blue) = color.ToRgb();
                WriteFraction(rgb, 3 * i, deepRgb, red);
                WriteFraction(rgb, (3 * i) + 1, deepRgb, green);
                WriteFraction(rgb, (3 * i) + 2, deepRgb, blue);
            }

            return;
        }

        if (!deepRgb)
        {
            ColorConversionKernels.Convert<byte, IccByteSample>(_pipeline, source, rgb, 4, 3);
            return;
        }

        ReadOnlySpan<ushort> samples;
        if (deepInks)
        {
            samples = unsafe(MemoryMarshal.Cast<byte, ushort>(source));
        }
        else
        {
            // 8-bit inks are widened exactly
            for (var i = 0; i < inks.Length; i++)
            {
                inks[i] = PixelConverter.To16Bit(source[i]);
            }

            samples = inks;
        }

        ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_pipeline, samples, unsafe(MemoryMarshal.Cast<byte, ushort>(rgb)), 4, 3);
    }

    private void ToCmyk(ReadOnlySpan<byte> rgb, Span<byte> destination, Span<ushort> inks)
    {
        var count = inks.Length / 4;
        var deepInks = _cmykFormat == PixelFormat.Cmyk64;
        var deepRgb = _rgbFormat == PixelFormat.Rgb48;
        if (_pipeline is null)
        {
            for (var i = 0; i < count; i++)
            {
                var color = Cmyk.FromRgb(ReadFraction(rgb, 3 * i, deepRgb), ReadFraction(rgb, (3 * i) + 1, deepRgb), ReadFraction(rgb, (3 * i) + 2, deepRgb));
                WriteFraction(destination, 4 * i, deepInks, color.C);
                WriteFraction(destination, (4 * i) + 1, deepInks, color.M);
                WriteFraction(destination, (4 * i) + 2, deepInks, color.Y);
                WriteFraction(destination, (4 * i) + 3, deepInks, color.K);
            }

            return;
        }

        if (!deepRgb)
        {
            ColorConversionKernels.Convert<byte, IccByteSample>(_pipeline, rgb, destination, 3, 4);
            return;
        }

        if (deepInks)
        {
            ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(rgb)), unsafe(MemoryMarshal.Cast<byte, ushort>(destination)), 3, 4);
            return;
        }

        // The 16-bit inks are rounded to 8 bits once
        ColorConversionKernels.Convert<ushort, IccUInt16Sample>(_pipeline, unsafe(MemoryMarshal.Cast<byte, ushort>(rgb)), inks, 3, 4);
        for (var i = 0; i < inks.Length; i++)
        {
            destination[i] = PixelConverter.To8Bit(inks[i]);
        }
    }
}
