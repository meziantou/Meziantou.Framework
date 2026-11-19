using Meziantou.Framework.Imaging;

namespace Meziantou.Framework.WebPlatform.Imaging;

/// <summary>The conversions between the 16-bit pixels of the surfaces and the 8-bit pixels of the bitmaps (RGBA, straight alpha).</summary>
internal static class PixelConversions
{
    /// <summary>
    /// Converts a 16-bit component to 8 bits: rounded to the nearest value, with a bias of 1/256 of a level, so that a coverage of
    /// a half (a tie) gives the same value whether it was rounded to 16 bits once (on the canvas) or twice (on a layer, then on
    /// the canvas).
    /// </summary>
    public static byte ToByte(ushort value) => (byte)(((value * 65280L) + (65535L * 129)) / (65535L * 256));

    /// <summary>
    /// Converts an 8-bit pixel (straight alpha) to the 16-bit pixel of its premultiplied 8-bit components, as a browser holds
    /// the pixels of a canvas: each color <c>c</c> of alpha <c>a</c> becomes <c>round(c a / 255)</c> (to nearest, ties upward),
    /// divided by the alpha. A straight 8-bit color rounded from such a component gives that component back.
    /// </summary>
    public static Rgba64 FromPremultipliedByte(Rgba32 pixel)
    {
        return pixel.A switch
        {
            0 => default,
            255 => new Rgba64((ushort)(pixel.R * 257), (ushort)(pixel.G * 257), (ushort)(pixel.B * 257), ushort.MaxValue),
            _ => new Rgba64(Convert(pixel.R, pixel.A), Convert(pixel.G, pixel.A), Convert(pixel.B, pixel.A), (ushort)(pixel.A * 257)),
        };

        static ushort Convert(byte color, byte alpha)
        {
            var premultiplied = ((color * alpha) + 127) / 255;
            return (ushort)(((premultiplied * 65535L) + (alpha / 2)) / alpha);
        }
    }

    /// <summary>Converts 16-bit pixels to 8-bit RGBA bytes.</summary>
    public static void ToBytes(ReadOnlySpan<Rgba64> pixels, Span<byte> destination)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            destination[(i * 4) + 0] = ToByte(pixel.R);
            destination[(i * 4) + 1] = ToByte(pixel.G);
            destination[(i * 4) + 2] = ToByte(pixel.B);
            destination[(i * 4) + 3] = ToByte(pixel.A);
        }
    }

    /// <summary>Converts 8-bit RGBA bytes to 16-bit pixels.</summary>
    public static void FromBytes(ReadOnlySpan<byte> bytes, Span<Rgba64> destination)
    {
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = new Rgba64((ushort)(bytes[(i * 4) + 0] * 257), (ushort)(bytes[(i * 4) + 1] * 257), (ushort)(bytes[(i * 4) + 2] * 257), (ushort)(bytes[(i * 4) + 3] * 257));
        }
    }
}
