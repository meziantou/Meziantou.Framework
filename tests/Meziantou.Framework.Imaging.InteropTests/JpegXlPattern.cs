using System.Buffers.Binary;
using System.Globalization;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// A deterministic test image for the JPEG XL interoperability tests: integer samples, row by row and channel after
/// channel (gray, gray and alpha, RGB or RGBA), written as a Netpbm PAM file for the reference encoder.
/// </summary>
internal sealed class JpegXlPattern
{
    private JpegXlPattern(int width, int height, int channels, int maxValue, int[] samples)
    {
        Width = width;
        Height = height;
        Channels = channels;
        MaxValue = maxValue;
        Samples = samples;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Gets the number of channels: 1 (gray), 2 (gray and alpha), 3 (RGB) or 4 (RGBA).</summary>
    public int Channels { get; }

    /// <summary>Gets the largest sample value: 2^bits - 1.</summary>
    public int MaxValue { get; }

    public int[] Samples { get; }

    public bool HasAlpha => Channels is 2 or 4;

    /// <summary>Gets a pattern by name.</summary>
    /// <summary>
    /// Gets the name of the files of a pattern encoded with some options. A long list of options is cut and completed with
    /// a digest: the reference tools do not open paths of more than 260 characters on Windows.
    /// </summary>
    public static string GetFileName(string patternName, string options)
    {
        var name = patternName + options.Replace(' ', '_').Replace('=', '_').Replace(';', '_');
        if (name.Length <= 48)
            return name;

        var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(options)));
        return name[..40] + "-" + digest[..8];
    }

    public static JpegXlPattern Get(string name) => name switch
    {
        // Smooth gradients with a texture: every predictor has something to predict
        "smooth-rgb8" => Create(97, 61, 3, 255, (x, y, c) => (x * 3) + (y * 5) + (c * 40) + (x * y / 7)),
        "smooth-rgba16" => Create(70, 50, 4, 65535, (x, y, c) => (x * 811) + (y * 1201) + (c * 9000) + (x * y)),
        "smooth-gray8" => Create(50, 40, 1, 255, (x, y, _) => (x * x) + (y * 3)),
        "smooth-gray16" => Create(45, 35, 1, 65535, (x, y, _) => (x * 1811) + (y * 701)),
        "smooth-graya8" => Create(33, 21, 2, 255, (x, y, c) => (x * 7) + (y * 11) + (c * 100)),
        "smooth-gray10" => Create(40, 30, 1, 1023, (x, y, _) => (x * 31) + (y * 17)),
        "smooth-rgb12" => Create(40, 30, 3, 4095, (x, y, c) => (x * 131) + (y * 117) + (c * 999)),

        // Pseudo-random samples: nothing is predictable, and fully transparent pixels keep a color
        "noise-rgba8" => Create(64, 48, 4, 255, (x, y, c) => c == 3 && ((x / 8) + (y / 8)) % 2 == 0 ? 0 : Hash(x, y, c)),

        // Five colors: a palette
        "palette-rgb8" => Create(80, 60, 3, 255, (x, y, c) => PaletteColors[(((x / 9) + ((y / 7) * 3)) % 5 * 3) + c]),

        // Larger than a group, in both directions
        "large-rgb8" => Create(600, 300, 3, 255, (x, y, c) => (x * 7) + (y * 13) + (c * 50) + (x * y / 37)),
        "large-rgba8" => Create(300, 520, 4, 255, (x, y, c) => (x * 31) ^ (y * 17) ^ (c * 91) ^ ((x * y) >> 3)),

        // Content for the lossy modes: smooth areas, sharp edges and a mild texture, larger than a group in both directions
        "photo-rgb16" => CreatePhoto(300, 280, 3, 65535),
        "photo-rgb8" => CreatePhoto(300, 280, 3, 255),
        "photo-rgba16" => CreatePhoto(300, 280, 4, 65535),
        "photo-rgba8" => CreatePhoto(300, 280, 4, 255),
        "photo-gray16" => CreatePhoto(300, 280, 1, 65535),
        "photo-gray8" => CreatePhoto(300, 280, 1, 255),
        "photo-small-rgb16" => CreatePhoto(61, 43, 3, 65535),

        // Wider than an LF group (2048 samples): several LF groups
        "photo-wide-rgb16" => CreatePhoto(2100, 140, 3, 65535),

        // Slow ramps, bands that vary in one direction only, long diagonal waves, noise and a bar with hard edges: each suits
        // another transform of the lossy mode, from 8x8 to 64x64 samples
        "waves-rgb8" => Create(320, 320, 3, 255, GetWave),

        // The same small shapes at many places, as the letters of a text: what the encoder stores once, as patches
        "text-rgb8" => CreateText(480, 320),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown pattern."),
    };

    /// <summary>Gets the expected sample of a pixel as the library returns it: RGBA, scaled exactly to 16 bits.</summary>
    public Rgba64 GetExpectedPixel(int x, int y)
    {
        var offset = ((y * Width) + x) * Channels;
        var gray = Channels <= 2;
        var red = Scale(Samples[offset]);
        var green = gray ? red : Scale(Samples[offset + 1]);
        var blue = gray ? red : Scale(Samples[offset + 2]);
        var alpha = HasAlpha ? Scale(Samples[offset + Channels - 1]) : ushort.MaxValue;
        return new Rgba64(red, green, blue, alpha);
    }

    /// <summary>Scales a sample to 16 bits, to the nearest value: what the library documents for the depths other than 8 and 16.</summary>
    public ushort Scale(int sample) => (ushort)((((long)sample * ushort.MaxValue) + (MaxValue / 2)) / MaxValue);

    /// <summary>Writes the pattern as a PAM file (big-endian samples above 8 bits).</summary>
    public byte[] ToPam() => WritePam(Width, Height, Channels, MaxValue, Samples);

    public static byte[] WritePam(int width, int height, int channels, int maxValue, ReadOnlySpan<int> samples)
    {
        var tupleType = channels switch
        {
            1 => "GRAYSCALE",
            2 => "GRAYSCALE_ALPHA",
            3 => "RGB",
            _ => "RGB_ALPHA",
        };

        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {width}\nHEIGHT {height}\nDEPTH {channels}\nMAXVAL {maxValue}\nTUPLTYPE {tupleType}\nENDHDR\n"));
        var bytesPerSample = maxValue > 255 ? 2 : 1;
        var data = new byte[header.Length + (samples.Length * bytesPerSample)];
        header.CopyTo(data, 0);
        for (var i = 0; i < samples.Length; i++)
        {
            if (bytesPerSample == 1)
            {
                data[header.Length + i] = (byte)samples[i];
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header.Length + (i * 2)), (ushort)samples[i]);
            }
        }

        return data;
    }

    private static ReadOnlySpan<byte> PaletteColors => [10, 200, 30, 250, 250, 0, 0, 0, 255, 128, 128, 128, 255, 0, 255];

    private static JpegXlPattern Create(int width, int height, int channels, int maxValue, Func<int, int, int, int> sample)
    {
        var samples = new int[width * height * channels];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    samples[index++] = sample(x, y, c) & maxValue;
                }
            }
        }

        return new JpegXlPattern(width, height, channels, maxValue, samples);
    }

    /// <summary>Smooth waves, a disc and bars with hard edges, and a texture of low amplitude; the alpha channel has a soft and a hard edge.</summary>
    private static JpegXlPattern CreatePhoto(int width, int height, int channels, int maxValue)
    {
        var colorChannels = channels >= 3 ? 3 : 1;
        var samples = new int[width * height * channels];
        var index = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var u = x / 300.0;
                var v = y / 280.0;
                var distance = Math.Sqrt(((u - 0.62) * (u - 0.62)) + ((v - 0.4) * (v - 0.4)));
                for (var c = 0; c < colorChannels; c++)
                {
                    var value = 0.5 + (0.28 * Math.Sin((u * 5.1) + (c * 1.3))) + (0.17 * Math.Cos((v * 7.3) - (c * 0.7) + (u * 2)));
                    if (distance < 0.18)
                    {
                        value = 0.15 + (0.25 * c);
                    }

                    if (x % 97 < 6 && y > height / 2)
                    {
                        value = c == 1 ? 0.95 : 0.1;
                    }

                    value += (Hash(x, y, c) - 127.5) / 255.0 * 0.03;
                    samples[index++] = (int)Math.Round(Math.Clamp(value, 0, 1) * maxValue, MidpointRounding.AwayFromZero);
                }

                if (channels is 2 or 4)
                {
                    var alpha = x < width / 4 ? 0 : Math.Clamp((u - 0.25) * 2.5, 0, 1);
                    samples[index++] = (int)Math.Round((y % 64 < 4 ? 1 : alpha) * maxValue, MidpointRounding.AwayFromZero);
                }
            }
        }

        return new JpegXlPattern(width, height, channels, maxValue, samples);
    }

    /// <summary>Rows of small shapes taken from a set of 14, in three colors, on a light background, beside a smooth area.</summary>
    private static JpegXlPattern CreateText(int width, int height)
    {
        var samples = new int[width * height * 3];
        for (var i = 0; i < samples.Length; i += 3)
        {
            samples[i] = 250;
            samples[i + 1] = 250;
            samples[i + 2] = 246;
        }

        ReadOnlySpan<byte> colors = [20, 20, 30, 180, 30, 30, 30, 60, 160];
        var line = 0;
        for (var top = 12; top + 12 < height; top += 15, line++)
        {
            var column = 0;
            for (var left = 10; left + 9 < width; left += 9, column++)
            {
                var glyph = Hash(column, line, 5) % 14;
                if (Hash(column, line, 6) % 7 == 0)
                    continue;

                for (var gy = 0; gy < 10; gy++)
                {
                    for (var gx = 0; gx < 7; gx++)
                    {
                        if (Hash(gx + (glyph * 16), gy, 7) < 115)
                        {
                            var offset = (((top + gy) * width) + left + gx) * 3;
                            samples[offset] = colors[line % 3 * 3];
                            samples[offset + 1] = colors[(line % 3 * 3) + 1];
                            samples[offset + 2] = colors[(line % 3 * 3) + 2];
                        }
                    }
                }
            }
        }

        for (var y = height * 5 / 8; y < height - 10; y++)
        {
            for (var x = width * 5 / 8; x < width - 10; x++)
            {
                var offset = ((y * width) + x) * 3;
                samples[offset] = (x * 3) & 255;
                samples[offset + 1] = (y * 2) & 255;
                samples[offset + 2] = (x + y) & 255;
            }
        }

        return new JpegXlPattern(width, height, 3, 255, samples);
    }

    private static int GetWave(int x, int y, int c)
    {
        var value = 40 + (c * 30) + ((x * (3 - c)) / 4) + ((y * (c + 1)) / 5);
        if (y < 96 && x >= 160)
        {
            value += Triangle(x, 32) * 2;
        }

        if (y is >= 96 and < 192 && x < 160)
        {
            value += Triangle(y, 24) * 2;
        }

        if (y >= 192 && x >= 192)
        {
            value += (int)((((ulong)((x * 7) + (y * 13) + (c * 5)) * 2654435761UL) >> 27) & 31);
        }

        if (y is >= 200 and < 216 && x is >= 32 and < 120)
        {
            value = 230 - (c * 40);
        }

        if (y < 192 && ((x / 64) + (y / 96)) % 2 == 0)
        {
            value += Triangle(x + y, 64) / 2;
        }

        return Math.Clamp(value, 0, 255);

        static int Triangle(int value, int period)
        {
            value %= 2 * period;
            return value < period ? value : (2 * period) - value;
        }
    }

    private static int Hash(int x, int y, int c)
    {
        var value = (uint)((x * 73856093) ^ (y * 19349663) ^ (c * 83492791));
        value ^= value >> 13;
        value *= 0x5BD1E995;
        value ^= value >> 15;
        return (int)(value & 0xFF);
    }
}
