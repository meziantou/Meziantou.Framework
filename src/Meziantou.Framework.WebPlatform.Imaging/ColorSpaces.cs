using System.Numerics;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>The conversions of the colors of the display lists (sRGB) to the color spaces of the bitmaps (sRGB or Display P3).</summary>
internal static class ColorSpaces
{
    /// <summary>The smallest alpha that is visible: a color whose alpha rounds to 0 in 8 bits is transparent.</summary>
    private const float MinVisibleAlpha = 0.5f / 255;

    /// <summary>Returns a paint color without clipping its gamut or rounding its samples.</summary>
    public static RgbaFloat ToRgbaFloat(DisplayColor color, RenderingColorSpace colorSpace)
    {
        var rgb = new Vector3(color.R, color.G, color.B);
        if (colorSpace == RenderingColorSpace.DisplayP3)
            rgb = SrgbToDisplayP3(rgb);

        return new RgbaFloat(rgb.X, rgb.Y, rgb.Z, float.IsNaN(color.A) ? 0 : Math.Clamp(color.A, 0, 1));
    }

    /// <summary>Returns a color (straight alpha) in the color space of a bitmap, with 16 bits per component.</summary>
    public static Rgba64 ToRgba64(DisplayColor color, RenderingColorSpace colorSpace)
    {
        // A color whose alpha rounds to 0 in 8 bits is transparent (it would otherwise move the colors it is composited over
        // by a fraction of a level, which shows when they are rounded)
        var alpha = float.IsNaN(color.A) || color.A < MinVisibleAlpha ? 0 : Math.Min(color.A, 1);
        var rgb = new Vector3(Sanitize(color.R), Sanitize(color.G), Sanitize(color.B));
        if (colorSpace == RenderingColorSpace.DisplayP3)
        {
            rgb = SrgbToDisplayP3(rgb);
        }

        return new Rgba64(ToUInt16(rgb.X), ToUInt16(rgb.Y), ToUInt16(rgb.Z), ToUInt16(alpha));

        static float Sanitize(float value) => float.IsNaN(value) ? 0 : value;
    }

    /// <summary>Converts a component from 0 to 1 to 16 bits (rounded to the nearest value).</summary>
    public static ushort ToUInt16(float value) => value >= 1 ? ushort.MaxValue : value > 0 ? (ushort)((value * ushort.MaxValue) + 0.5f) : (ushort)0;

    /// <summary>Converts a component from 0 to 1 to 8 bits (rounded to the nearest value).</summary>
    public static byte ToByte(float value) => value >= 1 ? (byte)255 : value > 0 ? (byte)((value * 255) + 0.5f) : (byte)0;

    // https://www.w3.org/TR/css-color-4/#color-conversion-code
    public static Vector3 SrgbToDisplayP3(Vector3 srgb)
    {
        var r = Linearize(srgb.X);
        var g = Linearize(srgb.Y);
        var b = Linearize(srgb.Z);
        return new Vector3(
            Encode((0.8224619687143623 * r) + (0.17753803128563775 * g)),
            Encode((0.03319419885096161 * r) + (0.9668058011490384 * g)),
            Encode((0.017082631106915046 * r) + (0.07239744066396346 * g) + (0.9105199282291515 * b)));
    }

    public static Vector3 DisplayP3ToSrgb(Vector3 p3)
    {
        var r = Linearize(p3.X);
        var g = Linearize(p3.Y);
        var b = Linearize(p3.Z);
        return new Vector3(
            Encode((1.2249401762805598 * r) - (0.22494017628055996 * g)),
            Encode((-0.042056954709688163 * r) + (1.0420569547096881 * g)),
            Encode((-0.019637554590334432 * r) - (0.07863604555063188 * g) + (1.0982736001409663 * b)));
    }

    /// <summary>Converts the pixels of an image from a color space to the other one (the colors outside of the gamut are clipped).</summary>
    public static void Convert(Span<Rgba32> pixels, RenderingColorSpace from, RenderingColorSpace to)
    {
        if (from == to)
            return;

        // The images have few distinct colors: the conversions are cached
        var cache = new Dictionary<uint, Rgba32>();
        foreach (ref var pixel in pixels)
        {
            if (pixel.A == 0)
                continue;

            var key = (uint)(pixel.R | (pixel.G << 8) | (pixel.B << 16));
            if (!cache.TryGetValue(key, out var converted))
            {
                var color = new Vector3(pixel.R, pixel.G, pixel.B) / 255;
                color = to == RenderingColorSpace.DisplayP3 ? SrgbToDisplayP3(color) : DisplayP3ToSrgb(color);
                converted = new Rgba32(ToByte(color.X), ToByte(color.Y), ToByte(color.Z), 255);
                if (cache.Count < 65536)
                {
                    cache.Add(key, converted);
                }
            }

            pixel = new Rgba32(converted.R, converted.G, converted.B, pixel.A);
        }
    }

    /// <summary>Converts the pixels of an image with 16 bits per component from a color space to the other one (the colors outside of the gamut are clipped).</summary>
    public static void Convert(Span<Rgba64> pixels, RenderingColorSpace from, RenderingColorSpace to)
    {
        if (from == to)
            return;

        foreach (ref var pixel in pixels)
        {
            if (pixel.A == 0)
                continue;

            var color = new Vector3(pixel.R, pixel.G, pixel.B) / ushort.MaxValue;
            color = to == RenderingColorSpace.DisplayP3 ? SrgbToDisplayP3(color) : DisplayP3ToSrgb(color);
            pixel = new Rgba64(ToUInt16(color.X), ToUInt16(color.Y), ToUInt16(color.Z), pixel.A);
        }
    }

    /// <summary>Converts half-float pixels without clipping colors outside the destination gamut.</summary>
    public static void Convert(Span<RgbaHalf> pixels, RenderingColorSpace from, RenderingColorSpace to)
    {
        if (from == to)
            return;

        foreach (ref var pixel in pixels)
        {
            var color = new Vector3((float)pixel.R, (float)pixel.G, (float)pixel.B);
            color = to == RenderingColorSpace.DisplayP3 ? SrgbToDisplayP3(color) : DisplayP3ToSrgb(color);
            pixel = new RgbaHalf((Half)color.X, (Half)color.Y, (Half)color.Z, pixel.A);
        }
    }

    public static double Linearize(double value)
    {
        var abs = Math.Abs(value);
        var result = abs <= 0.04045 ? abs / 12.92 : Math.Pow((abs + 0.055) / 1.055, 2.4);
        return value < 0 ? -result : result;
    }

    public static float Encode(double value)
    {
        var abs = Math.Abs(value);
        var result = abs <= 0.0031308 ? abs * 12.92 : (1.055 * Math.Pow(abs, 1 / 2.4)) - 0.055;
        return (float)(value < 0 ? -result : result);
    }
}
