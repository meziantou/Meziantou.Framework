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
