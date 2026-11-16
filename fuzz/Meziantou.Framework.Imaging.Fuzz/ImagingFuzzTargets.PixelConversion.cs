using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The pixel formats of light: the sources and the destinations of the conversions.</summary>
    private static readonly PixelFormat[] ConversionFormats = [.. PixelFormats.All.Where(format => !PixelFormats.IsCmyk(format))];

    /// <summary>
    /// Converts an image of any bytes between two pixel formats: a byte of source format, a byte of destination format, a
    /// byte of options (clipping, a background, a tone mapping), a byte of width and one of height, then the bytes of the
    /// pixels. The conversion fails only with an <see cref="UnsupportedImageFeatureException"/> (samples that are not
    /// numbers or out of range, alpha removed without background); a floating-point gray image converted to a color format
    /// has its level in the three colors, and the gray formats go back to themselves through the color format of their
    /// precision.
    /// </summary>
    private static void PixelConversionTarget(ReadOnlySpan<byte> data)
    {
        var source = ConversionFormats[FuzzInput.ReadByte(ref data) % ConversionFormats.Length];
        var destination = ConversionFormats[FuzzInput.ReadByte(ref data) % ConversionFormats.Length];
        var flags = FuzzInput.ReadByte(ref data);
        var width = 1 + (FuzzInput.ReadByte(ref data) % 16);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 4);
        var options = new PixelConversionOptions
        {
            OutOfRangeSamples = (flags & 1) != 0 ? OutOfRangeSampleHandling.Clip : OutOfRangeSampleHandling.Reject,
            BackgroundColor = (flags & 2) != 0 ? new Rgba64(1000, 30000, 65535) : null,
            ToneMapping = (flags & 4) != 0 ? HdrToneMappingOptions.Default : null,
        };

        using var image = ImportPixels(source, data, width, height);
        Image converted;
        try
        {
            converted = ConvertPixels(image, destination, options);
        }
        catch (UnsupportedImageFeatureException)
        {
            return;
        }

        using (converted)
        {
            FuzzAssert.True(converted.PixelFormat == destination && converted.Size == image.Size, "The conversion gives an image of the destination format and of the same size.");
            if (!PixelFormats.IsFloatingPoint(source) || !PixelFormats.IsGrayscale(source))
                return;

            // A floating-point gray level is repeated in the three colors, exactly
            using var colors = ConvertPixels(image, PixelFormat.RgbaFloat, options: null);
            var pixels = new RgbaFloat[width * height];
            ((Image<RgbaFloat>)colors).Frames[0].CopyPixelDataTo(pixels);
            foreach (var pixel in pixels)
            {
                FuzzAssert.True(SameBits(pixel.R, pixel.G) && SameBits(pixel.G, pixel.B), $"The gray level {pixel.R} is repeated in the three colors.");
            }

            // The gray formats go back to themselves through the color format of their precision
            var widened = PixelFormats.GetBitsPerComponent(source) == 32 ? PixelFormat.RgbaFloat : PixelFormat.RgbaHalf;
            using var wide = ConvertPixels(image, widened, options: null);
            using var back = ConvertPixels(wide, source, options: null);
            FuzzAssert.True(GetFrameBytes(image).AsSpan().SequenceEqual(GetFrameBytes(back)) || HasNaN(pixels), $"{source} goes back to itself through {widened}.");
        }

        static bool SameBits(float left, float right) => BitConverter.SingleToInt32Bits(left) == BitConverter.SingleToInt32Bits(right) || (float.IsNaN(left) && float.IsNaN(right));

        static bool HasNaN(RgbaFloat[] pixels) => pixels.Any(pixel => float.IsNaN(pixel.R) || float.IsNaN(pixel.A));
    }

    /// <summary>
    /// Converts CMYK inks of any bytes to RGB with the device formulas (a byte of width, 1 to 64, a byte of height, 1 to 3,
    /// then the inks, repeated when the input is shorter): every color is the one of <see cref="Cmyk.ToRgba32"/>, which
    /// evaluates the formulas in double precision. The inks also check the copies of the pixels: a clone has the bytes of
    /// the image, and a vertical flip puts each row at the mirrored row.
    /// </summary>
    private static void DeviceCmykTarget(ReadOnlySpan<byte> data)
    {
        var width = 1 + (FuzzInput.ReadByte(ref data) % 64);
        var height = 1 + (FuzzInput.ReadByte(ref data) % 3);
        using var inks = Create<Cmyk32>(data, width, height);
        using var colors = inks.CloneAs<Rgb24>(new PixelConversionOptions { CmykConversion = CmykPixelConversionOptions.Default });
        var source = new Cmyk32[width * height];
        var converted = new Rgb24[width * height];
        inks.Frames[0].CopyPixelDataTo(source);
        colors.Frames[0].CopyPixelDataTo(converted);
        for (var i = 0; i < source.Length; i++)
        {
            var ink = source[i];
            var expected = new Cmyk(ink.C / 255.0, ink.M / 255.0, ink.Y / 255.0, ink.K / 255.0).ToRgba32();
            FuzzAssert.True(converted[i].R == expected.R && converted[i].G == expected.G && converted[i].B == expected.B, $"The inks {ink.C}, {ink.M}, {ink.Y}, {ink.K} give the color of the device formulas.");
        }

        using var clone = inks.Clone();
        var bytes = GetFrameBytes(inks);
        FuzzAssert.True(bytes.AsSpan().SequenceEqual(GetFrameBytes(clone)), "A clone has the bytes of the image.");
        clone.Flip(FlipMode.Vertical);
        var flipped = GetFrameBytes(clone);
        var rowLength = width * 4;
        for (var y = 0; y < height; y++)
        {
            FuzzAssert.True(bytes.AsSpan(y * rowLength, rowLength).SequenceEqual(flipped.AsSpan((height - 1 - y) * rowLength, rowLength)), $"The row {y} is at the mirrored row after a vertical flip.");
        }
    }

    private static IEnumerable<FuzzSeed> GetDeviceCmykSeeds()
    {
        yield return new FuzzSeed("one-pixel", (byte[])[0, 0, 10, 20, 30, 40]);
        yield return new FuzzSeed("paper-and-full-black", (byte[])[1, 0, 0, 0, 0, 0, 255, 255, 255, 255]);
        yield return new FuzzSeed("vector-and-tail", (byte[])[16, 2, 0, 1, 2, 3, 127, 128, 129, 255, 7, 99, 200, 13, 254, 1, 64, 191]);
        yield return new FuzzSeed("wide-rows", (byte[])[63, 2, 255, 0, 128, 77, 3, 250, 6, 9, 12, 15, 18, 21, 24, 27]);
    }

    private static byte[] GetFrameBytes(Image image)
    {
        var frame = image.Frames[0];
        var bytes = new byte[frame.Width * frame.Height * PixelFormats.GetBytesPerPixel(frame.PixelFormat)];
        frame.CopyPixelBytesTo(bytes);
        return bytes;
    }

    private static Image ImportPixels(PixelFormat format, ReadOnlySpan<byte> data, int width, int height) => format switch
    {
        PixelFormat.Rgba32 => Create<Rgba32>(data, width, height),
        PixelFormat.Bgra32 => Create<Bgra32>(data, width, height),
        PixelFormat.Rgb24 => Create<Rgb24>(data, width, height),
        PixelFormat.Rgba64 => Create<Rgba64>(data, width, height),
        PixelFormat.Gray8 => Create<Gray8>(data, width, height),
        PixelFormat.Gray16 => Create<Gray16>(data, width, height),
        PixelFormat.RgbaFloat => Create<RgbaFloat>(data, width, height),
        PixelFormat.RgbaHalf => Create<RgbaHalf>(data, width, height),
        PixelFormat.La16 => Create<La16>(data, width, height),
        PixelFormat.La32 => Create<La32>(data, width, height),
        PixelFormat.Rgb48 => Create<Rgb48>(data, width, height),
        PixelFormat.A8 => Create<A8>(data, width, height),
        PixelFormat.Argb32 => Create<Argb32>(data, width, height),
        PixelFormat.Abgr32 => Create<Abgr32>(data, width, height),
        PixelFormat.Bgr565 => Create<Bgr565>(data, width, height),
        PixelFormat.Rgba1010102 => Create<Rgba1010102>(data, width, height),
        PixelFormat.GrayFloat => Create<GrayFloat>(data, width, height),
        PixelFormat.GrayHalf => Create<GrayHalf>(data, width, height),
        PixelFormat.LaFloat => Create<LaFloat>(data, width, height),
        PixelFormat.LaHalf => Create<LaHalf>(data, width, height),
        _ => throw new InvalidOperationException("The format is not listed."),
    };

    private static Image ConvertPixels(Image image, PixelFormat format, PixelConversionOptions? options) => format switch
    {
        PixelFormat.Rgba32 => image.CloneAs<Rgba32>(options),
        PixelFormat.Bgra32 => image.CloneAs<Bgra32>(options),
        PixelFormat.Rgb24 => image.CloneAs<Rgb24>(options),
        PixelFormat.Rgba64 => image.CloneAs<Rgba64>(options),
        PixelFormat.Gray8 => image.CloneAs<Gray8>(options),
        PixelFormat.Gray16 => image.CloneAs<Gray16>(options),
        PixelFormat.RgbaFloat => image.CloneAs<RgbaFloat>(options),
        PixelFormat.RgbaHalf => image.CloneAs<RgbaHalf>(options),
        PixelFormat.La16 => image.CloneAs<La16>(options),
        PixelFormat.La32 => image.CloneAs<La32>(options),
        PixelFormat.Rgb48 => image.CloneAs<Rgb48>(options),
        PixelFormat.A8 => image.CloneAs<A8>(options),
        PixelFormat.Argb32 => image.CloneAs<Argb32>(options),
        PixelFormat.Abgr32 => image.CloneAs<Abgr32>(options),
        PixelFormat.Bgr565 => image.CloneAs<Bgr565>(options),
        PixelFormat.Rgba1010102 => image.CloneAs<Rgba1010102>(options),
        PixelFormat.GrayFloat => image.CloneAs<GrayFloat>(options),
        PixelFormat.GrayHalf => image.CloneAs<GrayHalf>(options),
        PixelFormat.LaFloat => image.CloneAs<LaFloat>(options),
        PixelFormat.LaHalf => image.CloneAs<LaHalf>(options),
        _ => throw new InvalidOperationException("The format is not listed."),
    };

    private static IEnumerable<FuzzSeed> GetPixelConversionSeeds()
    {
        // The float gray formats are the last four of the list of conversion formats; the first byte picks the source
        var count = (byte)ConversionFormats.Length;
        var grayFloat = (byte)Array.IndexOf(ConversionFormats, PixelFormat.GrayFloat);
        var laHalf = (byte)Array.IndexOf(ConversionFormats, PixelFormat.LaHalf);
        var rgbaFloat = (byte)Array.IndexOf(ConversionFormats, PixelFormat.RgbaFloat);
        var gray8 = (byte)Array.IndexOf(ConversionFormats, PixelFormat.Gray8);
        yield return new FuzzSeed("grayfloat-to-gray8-clip", (byte[])[grayFloat, gray8, 1, 3, 0, 0, 0, 0x80, 0x3F, 0, 0, 0x20, 0x40, 0, 0, 0x80, 0xBF]);
        yield return new FuzzSeed("lahalf-to-rgbafloat", (byte[])[laHalf, rgbaFloat, 0, 1, 1, 0, 0x3C, 0, 0x38, 0, 0x7E, 0, 0x3C]);
        yield return new FuzzSeed("rgbafloat-to-grayfloat-background", (byte[])[rgbaFloat, grayFloat, 2, 0, 0, 0, 0, 0x80, 0x3F, 0, 0, 0, 0x3F, 0, 0, 0, 0, 0, 0, 0, 0x3F]);
        yield return new FuzzSeed("every-format", (byte[])[(byte)(count - 1), 0, 7, 7, 3, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 0xFF, 0x7F, 0x80]);
    }
}
