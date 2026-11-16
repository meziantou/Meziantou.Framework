using System.Buffers.Binary;

namespace Meziantou.Framework.Imaging.TestHarness.Psd;

/// <summary>The sections of an Adobe Photoshop file that <see cref="ReferencePsd"/> reads.</summary>
public sealed class PsdPicture
{
    public required int Version { get; init; }

    public required int Width { get; init; }

    public required int Height { get; init; }

    public required int Depth { get; init; }

    public required int Mode { get; init; }

    public required int ChannelCount { get; init; }

    public required ReadOnlyMemory<byte> ColorModeData { get; init; }

    /// <summary>Gets the image resources of the <c>8BIM</c> signature, by identifier (the first of each).</summary>
    public required IReadOnlyDictionary<int, byte[]> Resources { get; init; }

    /// <summary>Gets the layer count of the layer info (or of its <c>Layr</c>, <c>Lr16</c> or <c>Lr32</c> tagged block), or <see langword="null"/>.</summary>
    public required int? LayerCount { get; init; }

    public required int Compression { get; init; }

    /// <summary>Gets the decompressed samples of each channel, as stored (<c>Height</c> rows of <see cref="RowLength"/> bytes); empty for a ZIP composite.</summary>
    public required IReadOnlyList<byte[]> Channels { get; init; }

    public int RowLength => ((Width * Depth) + 7) / 8;

    public int ColorChannelCount => Mode switch
    {
        3 or 9 => 3,
        4 => 4,
        _ => 1,
    };

    /// <summary>Gets a value indicating whether the version information resource (1057) says the file has no real composite.</summary>
    public bool HasNoRealComposite => Resources.TryGetValue(1057, out var info) && info.Length >= 5 && info[4] == 0;

    /// <summary>Gets a value indicating whether the first extra channel is the transparency of the composite (a negative layer count).</summary>
    public bool HasMergedAlpha => LayerCount < 0 && ChannelCount > ColorChannelCount && Mode is not (0 or 2);

    public int? TransparentIndex => Mode == 2 && Resources.TryGetValue(1047, out var value) && value.Length >= 2 && BinaryPrimitives.ReadUInt16BigEndian(value) < 256 ? BinaryPrimitives.ReadUInt16BigEndian(value) : null;

    /// <summary>
    /// Gets the straight RGBA samples of the composite (bitmap, grayscale, duotone, indexed and RGB images of 1, 8 and 16
    /// bits): an 8-bit sample is one value in [0, 255], a 16-bit one in [0, 65535]. A bitmap sample of 1 is black, an index
    /// is its color of the table, a gray sample is repeated, and the colors of a composite with a transparency channel are
    /// un-matted from white: <c>round((s × max − max × (max − α)) / α)</c>, clipped; the stored sample where α is 0.
    /// </summary>
    public int[] GetRgba()
    {
        if (Depth == 32 || Mode is 4 or 9)
            throw new InvalidOperationException("The image has no integer RGBA reference.");

        var max = Depth == 16 ? 65535 : 255;
        var count = Width * Height;
        var rgba = new int[count * 4];
        for (var i = 0; i < count; i++)
        {
            var y = i / Width;
            var x = i % Width;
            int Sample(int channel) => Depth switch
            {
                1 => ((Channels[channel][(y * RowLength) + (x / 8)] >> (7 - (x % 8))) & 1) == 1 ? 0 : 255,
                8 => Channels[channel][(y * Width) + x],
                _ => BinaryPrimitives.ReadUInt16BigEndian(Channels[channel].AsSpan(((y * Width) + x) * 2)),
            };

            var pixel = rgba.AsSpan(i * 4, 4);
            if (Mode == 2)
            {
                var index = Sample(0);
                var table = ColorModeData.Span;
                pixel[0] = table[index];
                pixel[1] = table[256 + index];
                pixel[2] = table[512 + index];
                pixel[3] = index == TransparentIndex ? 0 : 255;
                continue;
            }

            var colors = ColorChannelCount;
            for (var c = 0; c < 3; c++)
            {
                pixel[c] = Sample(colors == 1 ? 0 : c);
            }

            pixel[3] = HasMergedAlpha ? Sample(colors) : max;
            for (var c = 0; c < 3; c++)
            {
                pixel[c] = Unmatte(pixel[c], pixel[3], max);
            }
        }

        return rgba;
    }

    /// <summary>Gets the RGBA floats of a 32-bit composite (gray repeated, colors un-matted from white without clipping; the stored ones where α is 0).</summary>
    public float[] GetRgbaFloat()
    {
        if (Depth != 32)
            throw new InvalidOperationException("The image has no floating-point samples.");

        var count = Width * Height;
        var rgba = new float[count * 4];
        for (var i = 0; i < count; i++)
        {
            float Sample(int channel) => BinaryPrimitives.ReadSingleBigEndian(Channels[channel].AsSpan(i * 4));
            var colors = ColorChannelCount;
            var alpha = HasMergedAlpha ? Sample(colors) : 1f;
            for (var c = 0; c < 3; c++)
            {
                var value = Sample(colors == 1 ? 0 : c);
                rgba[(i * 4) + c] = alpha is 1 or 0 ? value : (float)((value - (1.0 - alpha)) / alpha);
            }

            rgba[(i * 4) + 3] = alpha;
        }

        return rgba;
    }

    private static int Unmatte(int sample, int alpha, int max)
    {
        if (alpha == max)
            return sample;

        if (alpha == 0)
            return sample;

        var exact = ((double)sample * max - ((double)max * (max - alpha))) / alpha;
        return (int)Math.Clamp(Math.Floor(exact + 0.5), 0, max);
    }
}
