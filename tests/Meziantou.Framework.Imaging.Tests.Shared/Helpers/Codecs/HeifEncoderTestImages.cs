using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The sources of the tests of the encoders of the HEIF family (AVIF and HEIC), which have the same settings and the same
/// container: images of noise, which only a lossless coding gives back, and smooth images, which a lossy coding keeps close.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
internal static class HeifEncoderTestImages
{
    /// <summary>Creates an image of seeded noise; with <paramref name="transparent"/>, its alpha values are 0, 255, 128, 1 and 254 in turn.</summary>
    public static Image<Rgba32> CreateImage(int width, int height, int seed, bool transparent)
    {
        ReadOnlySpan<byte> alphas = [0, 255, 128, 1, 254];
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var alpha = transparent ? alphas[i % alphas.Length] : (byte)255;
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha);
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    /// <summary>
    /// Creates an image of smooth waves; <paramref name="phase"/> moves the red wave, so that the images of a sequence look
    /// like each other. With <paramref name="transparent"/>, the alpha values change at every pixel.
    /// </summary>
    public static Image<Rgba32> CreateSmoothImage(int width, int height, bool transparent, int phase = 0)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var r = (byte)(128 + (100 * Math.Sin((x + phase) / 11.0)));
                var g = (byte)(128 + (100 * Math.Cos((x + y) / 17.0)));
                var b = (byte)((x * 255 / width / 2) + (y * 255 / height / 2));
                pixels[(y * width) + x] = new Rgba32(r, g, b, transparent ? (byte)((x * 7) ^ (y * 13)) : byte.MaxValue);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    /// <summary>Gets the pixels of a frame of an image of four 8-bit samples per pixel.</summary>
    public static byte[] GetBytes(Image image, int frame = 0)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[frame].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
