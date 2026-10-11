using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>
/// Deterministic sources for the JPEG encoder tests: a photo-like synthetic image (smooth gradients, soft and
/// hard edges, fine texture and low-amplitude noise), and small patterns that global similarity measures cannot hide —
/// solid primary and secondary colors in MCU-aligned patches (channel order and range), and one-pixel stripes and
/// checkerboards in saturated complementary colors (chroma subsampling orientation and filter). Also the test-side
/// transcription of the encoder's input policy (alpha flattening at source precision, nearest 16-to-8-bit reduction).
/// </summary>
public static class JpegTestPatterns
{
    /// <summary>Gets the solid colors of <see cref="ColorPatches"/>, in patch order.</summary>
    public static IReadOnlyList<(byte R, byte G, byte B)> PatchColors { get; } =
    [
        (255, 0, 0), (0, 255, 0), (0, 0, 255), (0, 255, 255),
        (255, 0, 255), (255, 255, 0), (255, 255, 255), (0, 0, 0),
        (128, 128, 128), (200, 30, 90), (20, 160, 220), (250, 180, 40),
    ];

    /// <summary>Creates a photo-like synthetic RGB image.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="seed">The noise seed.</param>
    /// <returns>The pixels (<see cref="RawPixelLayout.Rgb8"/>).</returns>
    public static RawPixelBuffer PhotoLike(int width, int height, int seed)
    {
        var data = new byte[width * height * 3];
        var state = (uint)seed * 2654435761u + 1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = (x + 0.5) / width;
                var v = (y + 0.5) / height;

                // Sky-like vertical gradient, a soft "sun", a hard-edged dark "hill" and a fine texture on it
                var r = 70 + (120 * v);
                var g = 110 + (90 * v);
                var b = 220 - (60 * v);
                var sun = Math.Exp(-(((u - 0.7) * (u - 0.7)) + ((v - 0.3) * (v - 0.3))) * 40);
                r += 180 * sun;
                g += 150 * sun;
                b += 40 * sun;
                if (v > 0.65 + (0.12 * Math.Sin(u * 9)))
                {
                    var texture = 18 * Math.Sin((x * 1.7) + (y * 0.9)) * Math.Cos(y * 1.3);
                    r = 60 + (40 * u) + texture;
                    g = 120 + (30 * Math.Sin(v * 20)) + texture;
                    b = 40 + texture;
                }

                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var noise = ((int)(state % 9) - 4) * 0.75;
                var offset = ((y * width) + x) * 3;
                data[offset] = ToByte(r + noise);
                data[offset + 1] = ToByte(g + noise);
                data[offset + 2] = ToByte(b - noise);
            }
        }

        return RawPixelBuffer.Create(width, height, RawPixelLayout.Rgb8, data);
    }

    /// <summary>Creates 16x16 solid patches of <see cref="PatchColors"/>, four per row (64 pixels wide, 48 high), aligned with every MCU size.</summary>
    /// <returns>The pixels (<see cref="RawPixelLayout.Rgb8"/>).</returns>
    public static RawPixelBuffer ColorPatches()
    {
        const int Size = 16;
        var width = 4 * Size;
        var height = PatchColors.Count / 4 * Size;
        var data = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = PatchColors[((y / Size) * 4) + (x / Size)];
                var offset = ((y * width) + x) * 3;
                data[offset] = r;
                data[offset + 1] = g;
                data[offset + 2] = b;
            }
        }

        return RawPixelBuffer.Create(width, height, RawPixelLayout.Rgb8, data);
    }

    /// <summary>Creates one-pixel stripes or a checkerboard of two colors.</summary>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    /// <param name="first">The first color.</param>
    /// <param name="second">The second color.</param>
    /// <param name="selector">Selects the second color at (x, y), e.g. <c>(x, y) =&gt; x % 2 == 1</c> for vertical stripes.</param>
    /// <returns>The pixels (<see cref="RawPixelLayout.Rgb8"/>).</returns>
    public static RawPixelBuffer TwoColors(int width, int height, (byte R, byte G, byte B) first, (byte R, byte G, byte B) second, Func<int, int, bool> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        var data = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var (r, g, b) = selector(x, y) ? second : first;
                var offset = ((y * width) + x) * 3;
                data[offset] = r;
                data[offset + 1] = g;
                data[offset + 2] = b;
            }
        }

        return RawPixelBuffer.Create(width, height, RawPixelLayout.Rgb8, data);
    }

    /// <summary>Converts RGB pixels to gray with the test-side mean <c>(R + 2G + B + 2) / 4</c> (only used to build gray sources).</summary>
    /// <param name="source">The RGB pixels.</param>
    /// <returns>The gray pixels.</returns>
    public static RawPixelBuffer ToGray(RawPixelBuffer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var data = new byte[source.Width * source.Height];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                data[(y * source.Width) + x] = (byte)((source.GetSample(x, y, 0) + (2 * source.GetSample(x, y, 1)) + source.GetSample(x, y, 2) + 2) / 4);
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, RawPixelLayout.Gray8, data);
    }

    /// <summary>
    /// Builds the source buffer of a pixel format from 8-bit RGB or gray pixels: opaque alpha added, 8-bit samples expanded to
    /// 16 bits with <c>v * 257</c> plus <paramref name="lowBits"/> (so a 16-bit source does not reduce trivially), gray
    /// formats taken from <see cref="ToGray"/> when the input is RGB.
    /// </summary>
    /// <param name="pixels">The 8-bit pixels.</param>
    /// <param name="format">The target pixel format.</param>
    /// <param name="lowBits">A function of (x, y, channel) giving a signed offset added to 16-bit samples (clamped).</param>
    /// <returns>The buffer in the layout of <paramref name="format"/> (<c>ImageSnapshots.GetLayout</c>).</returns>
    public static RawPixelBuffer ToFormat(RawPixelBuffer pixels, PixelFormat format, Func<int, int, int, int>? lowBits = null)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        var gray = format is PixelFormat.Gray8 or PixelFormat.Gray16;
        if (gray && pixels.Layout != RawPixelLayout.Gray8)
        {
            pixels = ToGray(pixels);
        }

        if (!gray && pixels.Layout != RawPixelLayout.Rgb8)
            throw new ArgumentException("Color formats are built from RGB pixels.", nameof(pixels));

        var (layout, channels, sixteen) = format switch
        {
            PixelFormat.Rgba32 or PixelFormat.Bgra32 => (RawPixelLayout.Rgba8, 4, false),
            PixelFormat.Rgb24 => (RawPixelLayout.Rgb8, 3, false),
            PixelFormat.Rgba64 => (RawPixelLayout.Rgba16Le, 4, true),
            PixelFormat.Gray8 => (RawPixelLayout.Gray8, 1, false),
            _ => (RawPixelLayout.Gray16Le, 1, true),
        };

        var data = new byte[pixels.Width * pixels.Height * channels * (sixteen ? 2 : 1)];
        var sourceChannels = pixels.Layout.ChannelCount;
        for (var y = 0; y < pixels.Height; y++)
        {
            for (var x = 0; x < pixels.Width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var value = c < sourceChannels ? pixels.GetSample(x, y, c) : 255;
                    var index = (((y * pixels.Width) + x) * channels) + c;
                    if (sixteen)
                    {
                        var wide = c < sourceChannels ? Math.Clamp((value * 257) + (lowBits?.Invoke(x, y, c) ?? 0), 0, 65535) : 65535;
                        data[2 * index] = (byte)wide;
                        data[(2 * index) + 1] = (byte)(wide >> 8);
                    }
                    else
                    {
                        data[index] = (byte)value;
                    }
                }
            }
        }

        return RawPixelBuffer.Create(pixels.Width, pixels.Height, layout, data);
    }

    /// <summary>
    /// The encoder's input policy, transcribed from the documented conversion and alpha rules: alpha flattened onto the background at
    /// the source precision (<c>(s * a + bg * (max - a) + max / 2) / max</c>; the background reduced to 8 bits first for 8-bit
    /// sources), then 16-bit samples reduced to 8 bits with <c>(v * 255 + 32767) / 65535</c>.
    /// </summary>
    /// <param name="source">The source in a canonical layout (<c>rgba8</c>, <c>rgba16le</c>, <c>rgb8</c>, <c>gray8</c>, <c>gray16le</c>).</param>
    /// <param name="background">The 16-bit background (R, G, B), or <see langword="null"/> when every pixel is opaque.</param>
    /// <returns>The 8-bit samples the encoder transforms (<see cref="RawPixelLayout.Rgb8"/> or <see cref="RawPixelLayout.Gray8"/>).</returns>
    public static RawPixelBuffer ToEncodedSamples(RawPixelBuffer source, (ushort R, ushort G, ushort B)? background)
    {
        ArgumentNullException.ThrowIfNull(source);
        var gray = source.Layout == RawPixelLayout.Gray8 || source.Layout == RawPixelLayout.Gray16Le;
        var sixteen = source.Layout.BytesPerSample == 2;
        var max = sixteen ? 65535 : 255;
        var hasAlpha = source.Layout == RawPixelLayout.Rgba8 || source.Layout == RawPixelLayout.Rgba16Le;
        var channels = gray ? 1 : 3;
        int[] bg = background is { } color
            ? sixteen ? [color.R, color.G, color.B] : [Reduce(color.R), Reduce(color.G), Reduce(color.B)]
            : [0, 0, 0];
        var data = new byte[source.Width * source.Height * channels];
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var alpha = hasAlpha ? source.GetSample(x, y, 3) : max;
                if (alpha != max && background is null)
                    throw new InvalidOperationException("A non-opaque pixel needs a background.");

                for (var c = 0; c < channels; c++)
                {
                    long value = source.GetSample(x, y, c);
                    if (alpha != max)
                    {
                        value = ((value * alpha) + ((long)bg[c] * (max - alpha)) + (max / 2)) / max;
                    }

                    data[(((y * source.Width) + x) * channels) + c] = sixteen ? (byte)Reduce((int)value) : (byte)value;
                }
            }
        }

        return RawPixelBuffer.Create(source.Width, source.Height, gray ? RawPixelLayout.Gray8 : RawPixelLayout.Rgb8, data);

        static int Reduce(int value) => ((value * 255) + 32767) / 65535;
    }

    private static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
}
